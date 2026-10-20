# The Solo's Ascent — Hunter System

A Solo Leveling–themed self-improvement app. Complete daily quests to earn XP and gold,
level up your hunter and skills, climb from Rank E to SSS, and race your friends on the
guild leaderboard. Built with **Expo (React Native)** for iOS, Android and web, with
**Supabase** for login and cloud saves.

## Try it right now (no setup)

Open **`web-preview/solos-ascent.html`** in any browser (download it from GitHub, then
double-click it). It's the full app in a single file, running in **demo mode**: there's no
login, progress is saved in that browser, and the guild has simulated rivals. Tap
**Enter as Guest** to start.

## Features

| Tab | What it does |
|---|---|
| ⚔️ **Quests** | Daily quests reset at midnight. Tap to complete, long-press to edit, **+ NEW** to add your own. Clearing every quest gives a bonus and extends your 🔥 streak. Weekly raid: 7 clean days in a row. |
| 📊 **Status** | Hunter card, level and rank, stats, a 15-week quest calendar, and skill levels. Advanced skills (Mindset, Nutrition, Social, Finance, Creativity) **unlock as your rank rises** (D, C, B, A, S). |
| ❤️ **Vitals** | Steps, active calories, sleep and water. Syncs from **Apple Health** (iOS) or **Health Connect** (Android), or you can log them by hand. Quests marked ⚡ complete themselves when a vital reaches its goal. |
| 🛡️ **Shop** | Spend gold on color themes, avatar frames and **Streak Shields**. |
| 🏆 **Guild** | Add friends by hunter code (like `HNTR-4F9K`), accept requests, and see the leaderboard. |

**Penalty system.** If you miss a day's quests, the next day starts with a **Penalty
Quest**. Clear it that day, or at midnight you lose `50 + 25 × level` XP (you can never lose
a level). A Streak Shield, if you own one, absorbs a missed day instead.

**Reminders** (phone only): "Daily Quest has arrived" at 08:00, and a penalty warning at
20:00 if quests are still unfinished. You can change both times in ⚙️ Settings.

**Level-up / rank-up** shows a full-screen System window.

---

## Setup guide (for beginners)

### 1. Install the tools

1. Install **Node.js LTS** from <https://nodejs.org>.
2. Install **Git** from <https://git-scm.com>.
3. Create a free **Expo account** at <https://expo.dev/signup>.
4. Get the code and install its dependencies:
   ```bash
   git clone https://github.com/swiine/Detail_Grid.git
   cd Detail_Grid
   npm install
   ```

### 2. Run it on your computer (web, demo mode)

```bash
npm run web
```

The app opens at <http://localhost:8081>. With no Supabase keys it runs in demo mode.

### 3. Set up Supabase (accounts and cloud saves)

1. Create a free project at <https://supabase.com/dashboard>.
2. In the dashboard, open **SQL Editor → New query**, paste all of
   [`supabase/migrations/0001_init.sql`](supabase/migrations/0001_init.sql), and click **Run**.
3. Go to **Project Settings → API** and copy the **Project URL** and the **anon public** key.
4. In this folder, copy `.env.example` to `.env` and paste them in:
   ```
   EXPO_PUBLIC_SUPABASE_URL=https://xxxx.supabase.co
   EXPO_PUBLIC_SUPABASE_ANON_KEY=eyJ...
   ```
   (The anon key is designed to live inside apps, so it's safe here. Never put the
   `service_role` key in the app.)
5. Go to **Authentication → URL Configuration → Redirect URLs** and add:
   - `solosascent://auth-callback` (the phone app)
   - `http://localhost:8081` (local web)
6. Restart `npm run web`. You'll now see real sign-in instead of "Enter as Guest".

**Email + password** works immediately. By default, Supabase asks new users to confirm
their email; you can turn that off under **Authentication → Providers → Email**.

**Google sign-in:**
1. In Google Cloud Console → **APIs & Services → Credentials**, create an
   **OAuth client ID** of type **Web application**.
2. Under **Authorized redirect URIs**, add
   `https://<your-project>.supabase.co/auth/v1/callback`.
3. In Supabase → **Authentication → Providers → Google**, enable it and paste the
   client ID and secret.

**Apple sign-in** (needs a paid Apple Developer account, $99/yr):
- On iOS the app uses the native Apple button. In Supabase → **Providers → Apple**,
  enable it and add your bundle ID `com.solosascent.app` to the client IDs.
- For Apple sign-in on Android and web, also follow Supabase's
  ["Sign in with Apple" guide](https://supabase.com/docs/guides/auth/social-login/auth-apple)
  to create a Services ID and secret key.

### 4. Run it on your phone

Health sync, reminders and native Apple sign-in use native code, so the app needs a
**development build**. The **Expo Go** app from the store can't run it. EAS builds it in
the cloud, so you don't need Xcode or Android Studio.

```bash
npx eas-cli@latest login
npx eas-cli@latest build:configure          # first time only
npx eas-cli@latest build --profile development --platform android   # or ios
```

When the build finishes, open the link on your phone to install it. Then start the dev
server and open the app:

```bash
npx expo start
```

- **Android:** install the **Health Connect** app if your phone doesn't have it built in
  (Android 14+ includes it). The app asks for permission the first time you sync.
- **iPhone:** you need an Apple Developer account to install builds. Apple Health only
  works on a real device, not the simulator.

When you're ready for the stores: `npx eas-cli@latest build --profile production`, then
`npx eas-cli@latest submit`.

### 5. Rebuild the single-file web preview

```bash
npm run build:html     # writes web-preview/solos-ascent.html
```

If `.env` has Supabase keys when you build, the file uses real login. Otherwise it's
demo mode.

---

## Developer notes

```bash
npm test           # game-logic unit tests (rollover, penalties, shields, shop...)
npm run typecheck
npm run lint
```

| Path | Contents |
|---|---|
| `app/` | Screens (Expo Router). `(tabs)/` holds the five tabs; `sign-in`, `onboarding`, `quest-editor` and `settings` are stack screens. |
| `lib/game.ts` | All game rules as pure functions: XP curve, ranks, quests, rollover, penalties, shop. |
| `lib/backend.ts` | Supabase backend plus a local demo backend, chosen automatically. |
| `lib/health*.ts` | `.ios.ts` = HealthKit, `.android.ts` = Health Connect, `.ts` = web fallback. |
| `lib/notifications*.ts` | Local reminder scheduling (no-op on web). |
| `state/store.tsx` | App state: auth, loading/saving, day rollover, health sync, reminders. |
| `supabase/migrations/` | Database schema, row-level security and the friend-request function. |
| `scripts/build-single-html.mjs` | Packs the web export into one HTML file. |

**Known limitations**
- Leaderboard XP is reported by each player's app, so a determined cheater could fake
  it. That's fine for a friends leaderboard; a public one would need server-side checks.
- Web has no health sync or reminders; you log vitals manually there.
- The app icon and splash screen are still Expo's defaults. Replace the files in
  `assets/images/`.
