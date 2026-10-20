import * as AppleAuthentication from 'expo-apple-authentication';
import * as Crypto from 'expo-crypto';
import * as Linking from 'expo-linking';
import * as WebBrowser from 'expo-web-browser';
import { Platform } from 'react-native';

import { migrate } from './game';
import { getJSON, setJSON, storage } from './storage';
import { supabase } from './supabase';
import type { FriendRequest, GameState, PublicProfile } from './types';

WebBrowser.maybeCompleteAuthSession();

export interface AuthUser {
  id: string;
  email: string | null;
  /** Name from the identity provider, used to prefill onboarding. */
  suggestedName: string;
}

export interface Backend {
  mode: 'cloud' | 'demo';
  getUser(): Promise<AuthUser | null>;
  onAuthChange(cb: (u: AuthUser | null) => void): () => void;
  signInEmail(email: string, password: string): Promise<void>;
  /** Returns true if the user must confirm their email before signing in. */
  signUpEmail(email: string, password: string): Promise<boolean>;
  signInOAuth(provider: 'google' | 'apple'): Promise<void>;
  signInDemo(): Promise<void>;
  signOut(): Promise<void>;
  loadState(): Promise<GameState | null>;
  saveState(s: GameState): Promise<void>;
  myProfile(): Promise<PublicProfile | null>;
  sendFriendRequest(code: string): Promise<'sent' | 'accepted' | 'exists'>;
  friendRequests(): Promise<FriendRequest[]>;
  respondFriendRequest(id: string, accept: boolean): Promise<void>;
  leaderboard(): Promise<PublicProfile[]>;
  removeFriend(profileId: string): Promise<void>;
}

// ---------------------------------------------------------------------------
// Cloud (Supabase)
// ---------------------------------------------------------------------------

type ProfileRow = {
  id: string;
  hunter_name: string;
  friend_code: string;
  level: number;
  total_xp: number;
  streak: number;
  frame: string;
};

const toProfile = (r: ProfileRow): PublicProfile => ({
  id: r.id,
  hunterName: r.hunter_name || 'Unnamed Hunter',
  friendCode: r.friend_code,
  lv: r.level,
  totalXp: r.total_xp,
  streak: r.streak,
  frame: r.frame,
});

function cloud(): Backend {
  const sb = supabase!;
  const toUser = (u: { id: string; email?: string | null; user_metadata?: Record<string, unknown> } | null | undefined) =>
    u
      ? {
          id: u.id,
          email: u.email ?? null,
          suggestedName: String(u.user_metadata?.full_name ?? u.user_metadata?.name ?? ''),
        }
      : null;
  const uidOrThrow = async () => {
    const { data } = await sb.auth.getUser();
    if (!data.user) throw new Error('Not signed in.');
    return data.user.id;
  };
  const check = <T,>(res: { data: T; error: { message: string } | null }) => {
    if (res.error) throw new Error(res.error.message);
    return res.data;
  };

  return {
    mode: 'cloud',
    async getUser() {
      const { data } = await sb.auth.getSession();
      return toUser(data.session?.user);
    },
    onAuthChange(cb) {
      const { data } = sb.auth.onAuthStateChange((_e, session) => cb(toUser(session?.user)));
      return () => data.subscription.unsubscribe();
    },
    async signInEmail(email, password) {
      const { error } = await sb.auth.signInWithPassword({ email, password });
      if (error) throw new Error(error.message);
    },
    async signUpEmail(email, password) {
      const { data, error } = await sb.auth.signUp({ email, password });
      if (error) throw new Error(error.message);
      return !data.session;
    },
    async signInOAuth(provider) {
      // Native Sign in with Apple on iOS (required by App Review when offering social login).
      if (provider === 'apple' && Platform.OS === 'ios') {
        const rawNonce = Crypto.randomUUID();
        const hashed = await Crypto.digestStringAsync(Crypto.CryptoDigestAlgorithm.SHA256, rawNonce);
        const cred = await AppleAuthentication.signInAsync({
          requestedScopes: [
            AppleAuthentication.AppleAuthenticationScope.FULL_NAME,
            AppleAuthentication.AppleAuthenticationScope.EMAIL,
          ],
          nonce: hashed,
        });
        if (!cred.identityToken) throw new Error('Apple did not return an identity token.');
        const { error } = await sb.auth.signInWithIdToken({ provider: 'apple', token: cred.identityToken, nonce: rawNonce });
        if (error) throw new Error(error.message);
        const name = [cred.fullName?.givenName, cred.fullName?.familyName].filter(Boolean).join(' ');
        if (name) await sb.auth.updateUser({ data: { full_name: name } });
        return;
      }
      if (Platform.OS === 'web') {
        const { error } = await sb.auth.signInWithOAuth({ provider, options: { redirectTo: window.location.origin } });
        if (error) throw new Error(error.message);
        return; // The page redirects away and Supabase finishes sign-in on return.
      }
      const redirectTo = Linking.createURL('auth-callback');
      const { data, error } = await sb.auth.signInWithOAuth({ provider, options: { redirectTo, skipBrowserRedirect: true } });
      if (error) throw new Error(error.message);
      const res = await WebBrowser.openAuthSessionAsync(data.url, redirectTo);
      if (res.type !== 'success') return; // cancelled
      const { queryParams } = Linking.parse(res.url);
      const code = queryParams?.code;
      if (typeof code !== 'string') throw new Error(String(queryParams?.error_description ?? 'Sign-in failed.'));
      const ex = await sb.auth.exchangeCodeForSession(code);
      if (ex.error) throw new Error(ex.error.message);
    },
    async signInDemo() {
      throw new Error('Demo mode is only available when Supabase is not configured.');
    },
    async signOut() {
      await sb.auth.signOut();
    },
    async loadState() {
      const id = await uidOrThrow();
      const row = check(await sb.from('hunter_state').select('state').eq('user_id', id).maybeSingle());
      return row ? migrate(row.state as GameState) : null;
    },
    async saveState(s) {
      const id = await uidOrThrow();
      check(await sb.from('hunter_state').upsert({ user_id: id, state: s, updated_at: new Date().toISOString() }));
      check(
        await sb
          .from('profiles')
          .update({ hunter_name: s.hunterName, level: s.lv, total_xp: s.totalXp, streak: s.streak, frame: s.inventory.frame })
          .eq('id', id),
      );
    },
    async myProfile() {
      const id = await uidOrThrow();
      const row = check(await sb.from('profiles').select('*').eq('id', id).maybeSingle());
      return row ? toProfile(row as ProfileRow) : null;
    },
    async sendFriendRequest(code) {
      return check(await sb.rpc('send_friend_request', { code })) as 'sent' | 'accepted' | 'exists';
    },
    async friendRequests() {
      const id = await uidOrThrow();
      const rows = check(
        await sb
          .from('friendships')
          .select('id, from:profiles!friendships_requester_fkey(*)')
          .eq('addressee', id)
          .eq('status', 'pending'),
      ) as unknown as { id: string; from: ProfileRow }[];
      return rows.map((r) => ({ id: r.id, from: toProfile(r.from) }));
    },
    async respondFriendRequest(id, accept) {
      if (accept) check(await sb.from('friendships').update({ status: 'accepted' }).eq('id', id));
      else check(await sb.from('friendships').delete().eq('id', id));
    },
    async leaderboard() {
      const me = await uidOrThrow();
      const links = check(
        await sb.from('friendships').select('requester, addressee').eq('status', 'accepted').or(`requester.eq.${me},addressee.eq.${me}`),
      ) as { requester: string; addressee: string }[];
      const ids = [me, ...links.map((l) => (l.requester === me ? l.addressee : l.requester))];
      const rows = check(await sb.from('profiles').select('*').in('id', ids)) as ProfileRow[];
      return rows.map(toProfile).sort((a, b) => b.totalXp - a.totalXp);
    },
    async removeFriend(profileId) {
      const me = await uidOrThrow();
      check(
        await sb
          .from('friendships')
          .delete()
          .or(`and(requester.eq.${me},addressee.eq.${profileId}),and(requester.eq.${profileId},addressee.eq.${me})`),
      );
    },
  };
}

// ---------------------------------------------------------------------------
// Demo (local only, no account)
// ---------------------------------------------------------------------------

const K = { user: 'demo:user', state: 'demo:state', friends: 'demo:friends', requests: 'demo:requests' };

const CODE_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
export const randomCode = () =>
  'HNTR-' + Array.from({ length: 4 }, () => CODE_CHARS[Math.floor(Math.random() * CODE_CHARS.length)]).join('');

const RIVAL_NAMES = ['Kai of Shadows', 'IronVeil', 'Mira.exe', 'Ronan Blackgate', 'Yuna Frost', 'Dex the Unbroken'];
const fakeHunter = (name: string, lv: number): PublicProfile => ({
  id: 'demo-' + Math.random().toString(36).slice(2, 8),
  hunterName: name,
  friendCode: randomCode(),
  lv,
  totalXp: Array.from({ length: lv - 1 }, (_, i) => 250 + 150 * (i + 1)).reduce((a, b) => a + b, 0) + Math.floor(Math.random() * 300),
  streak: Math.floor(Math.random() * 20),
  frame: ['none', 'flame', 'frost', 'shadow'][Math.floor(Math.random() * 4)],
});

function demo(): Backend {
  const listeners = new Set<(u: AuthUser | null) => void>();
  const emit = (u: AuthUser | null) => listeners.forEach((l) => l(u));
  const me = async () => getJSON<AuthUser & { friendCode: string }>(K.user);
  const friends = async () => (await getJSON<PublicProfile[]>(K.friends)) ?? [];

  return {
    mode: 'demo',
    async getUser() {
      return me();
    },
    onAuthChange(cb) {
      listeners.add(cb);
      return () => listeners.delete(cb);
    },
    async signInEmail() {
      throw new Error('Cloud login is not configured. Use “Enter as Guest”.');
    },
    async signUpEmail() {
      throw new Error('Cloud login is not configured. Use “Enter as Guest”.');
    },
    async signInOAuth() {
      throw new Error('Cloud login is not configured. Use “Enter as Guest”.');
    },
    async signInDemo() {
      const existing = await me();
      const user = existing ?? { id: 'demo-me', email: null, suggestedName: '', friendCode: randomCode() };
      await setJSON(K.user, user);
      if (!existing) {
        await setJSON(K.friends, [fakeHunter(RIVAL_NAMES[0], 4), fakeHunter(RIVAL_NAMES[1], 2)]);
        await setJSON(K.requests, [{ id: 'req-1', from: fakeHunter(RIVAL_NAMES[2], 3) }]);
      }
      emit(user);
    },
    async signOut() {
      await storage.removeItem(K.user);
      emit(null);
    },
    async loadState() {
      const raw = await getJSON<GameState>(K.state);
      return raw ? migrate(raw) : null;
    },
    async saveState(s) {
      await setJSON(K.state, s);
    },
    async myProfile() {
      const u = await me();
      const s = await getJSON<GameState>(K.state);
      if (!u || !s) return null;
      return { id: u.id, hunterName: s.hunterName, friendCode: u.friendCode, lv: s.lv, totalXp: s.totalXp, streak: s.streak, frame: s.inventory.frame };
    },
    async sendFriendRequest(code) {
      const u = await me();
      const c = code.trim().toUpperCase();
      if (!/^HNTR-[A-Z0-9]{4}$/.test(c)) throw new Error('Codes look like HNTR-4F9K.');
      if (u?.friendCode === c) throw new Error('That is your own code.');
      const list = await friends();
      if (list.some((f) => f.friendCode === c)) return 'exists';
      // Demo: pretend the hunter instantly accepts.
      const f = { ...fakeHunter(RIVAL_NAMES[Math.floor(Math.random() * RIVAL_NAMES.length)], 1 + Math.floor(Math.random() * 8)), friendCode: c };
      await setJSON(K.friends, [...list, f]);
      return 'accepted';
    },
    async friendRequests() {
      return (await getJSON<FriendRequest[]>(K.requests)) ?? [];
    },
    async respondFriendRequest(id, accept) {
      const reqs = (await getJSON<FriendRequest[]>(K.requests)) ?? [];
      const r = reqs.find((x) => x.id === id);
      await setJSON(K.requests, reqs.filter((x) => x.id !== id));
      if (r && accept) await setJSON(K.friends, [...(await friends()), r.from]);
    },
    async leaderboard() {
      const mine = await this.myProfile();
      return [...(await friends()), ...(mine ? [mine] : [])].sort((a, b) => b.totalXp - a.totalXp);
    },
    async removeFriend(profileId) {
      await setJSON(K.friends, (await friends()).filter((f) => f.id !== profileId));
    },
  };
}

export const backend: Backend = supabase ? cloud() : demo();
