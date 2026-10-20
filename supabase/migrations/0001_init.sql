-- The Solo's Ascent — initial schema.
-- Paste this whole file into Supabase Dashboard → SQL Editor → New query → Run.

-- ---------------------------------------------------------------------------
-- Public profile: what friends can see on the leaderboard.
-- ---------------------------------------------------------------------------
create table if not exists public.profiles (
  id          uuid primary key references auth.users (id) on delete cascade,
  hunter_name text not null default '',
  friend_code text not null unique,
  level       int  not null default 1,
  total_xp    int  not null default 0,
  streak      int  not null default 0,
  frame       text not null default 'none',
  updated_at  timestamptz not null default now()
);

-- Private game state (quests, history, inventory...). Only the owner can read it.
create table if not exists public.hunter_state (
  user_id    uuid primary key references auth.users (id) on delete cascade,
  state      jsonb not null,
  updated_at timestamptz not null default now()
);

create table if not exists public.friendships (
  id         uuid primary key default gen_random_uuid(),
  requester  uuid not null references public.profiles (id) on delete cascade,
  addressee  uuid not null references public.profiles (id) on delete cascade,
  status     text not null default 'pending' check (status in ('pending', 'accepted')),
  created_at timestamptz not null default now(),
  unique (requester, addressee),
  check (requester <> addressee)
);
create index if not exists friendships_addressee_idx on public.friendships (addressee);

-- ---------------------------------------------------------------------------
-- Friend codes like HNTR-4F9K (no 0/O/1/I to avoid confusion).
-- ---------------------------------------------------------------------------
create or replace function public.generate_friend_code()
returns text
language plpgsql
as $$
declare
  chars constant text := 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  code text;
begin
  loop
    code := 'HNTR-';
    for i in 1..4 loop
      code := code || substr(chars, 1 + floor(random() * length(chars))::int, 1);
    end loop;
    exit when not exists (select 1 from public.profiles where friend_code = code);
  end loop;
  return code;
end;
$$;

-- Create a profile automatically for every new auth user.
create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer set search_path = public
as $$
begin
  insert into public.profiles (id, hunter_name, friend_code)
  values (new.id, coalesce(new.raw_user_meta_data ->> 'full_name', ''), public.generate_friend_code());
  return new;
end;
$$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();

-- ---------------------------------------------------------------------------
-- Row level security
-- ---------------------------------------------------------------------------
alter table public.profiles     enable row level security;
alter table public.hunter_state enable row level security;
alter table public.friendships  enable row level security;

-- Profiles: any signed-in hunter can read (needed to show friends and requests);
-- you can only update your own. Friend codes can't be changed from the client.
drop policy if exists "profiles readable" on public.profiles;
create policy "profiles readable" on public.profiles
  for select to authenticated using (true);

drop policy if exists "profiles update own" on public.profiles;
create policy "profiles update own" on public.profiles
  for update to authenticated using (id = auth.uid()) with check (id = auth.uid());

revoke update on public.profiles from authenticated, anon;
grant update (hunter_name, level, total_xp, streak, frame, updated_at) on public.profiles to authenticated;

-- Game state: owner only.
drop policy if exists "state owner" on public.hunter_state;
create policy "state owner" on public.hunter_state
  for all to authenticated using (user_id = auth.uid()) with check (user_id = auth.uid());

-- Friendships: both sides can see and delete; only the addressee can accept.
-- Inserts go through send_friend_request() below.
drop policy if exists "friendships visible to members" on public.friendships;
create policy "friendships visible to members" on public.friendships
  for select to authenticated using (auth.uid() in (requester, addressee));

drop policy if exists "friendships accept" on public.friendships;
create policy "friendships accept" on public.friendships
  for update to authenticated using (addressee = auth.uid()) with check (addressee = auth.uid() and status = 'accepted');

drop policy if exists "friendships delete" on public.friendships;
create policy "friendships delete" on public.friendships
  for delete to authenticated using (auth.uid() in (requester, addressee));

-- ---------------------------------------------------------------------------
-- send_friend_request(code): returns 'sent', 'accepted' (they had already asked you)
-- or 'exists'.
-- ---------------------------------------------------------------------------
create or replace function public.send_friend_request(code text)
returns text
language plpgsql
security definer set search_path = public
as $$
declare
  me uuid := auth.uid();
  target uuid;
begin
  if me is null then
    raise exception 'Not signed in';
  end if;

  select id into target from public.profiles where friend_code = upper(trim(code));
  if target is null then
    raise exception 'No hunter found with code %', upper(trim(code));
  end if;
  if target = me then
    raise exception 'That is your own code';
  end if;

  if exists (
    select 1 from public.friendships
    where (requester = me and addressee = target)
       or (requester = target and addressee = me and status = 'accepted')
  ) then
    return 'exists';
  end if;

  update public.friendships set status = 'accepted'
  where requester = target and addressee = me and status = 'pending';
  if found then
    return 'accepted';
  end if;

  insert into public.friendships (requester, addressee) values (me, target);
  return 'sent';
end;
$$;

revoke all on function public.send_friend_request(text) from public, anon;
grant execute on function public.send_friend_request(text) to authenticated;
