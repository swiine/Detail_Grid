import * as Haptics from 'expo-haptics';
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { AppState, Platform } from 'react-native';

import { backend, type AuthUser } from '@/lib/backend';
import {
  getRank,
  localDate,
  newGameState,
  penaltyLoss,
  rollover,
  setVitals,
  skillDef,
  type GameEvent,
} from '@/lib/game';
import { healthSupported, readTodayVitals } from '@/lib/health';
import { scheduleReminders } from '@/lib/notifications';
import { palette, type Palette } from '@/lib/theme';
import type { GameState, Vitals } from '@/lib/types';

type Updater = (s: GameState) => GameState | { state: GameState; events: GameEvent[] };

export interface LevelUpInfo {
  lv: number;
  rankUp: boolean;
}

interface Store {
  ready: boolean;
  loadError: string | null;
  retryLoad: () => void;
  user: AuthUser | null;
  game: GameState | null;
  pal: Palette;
  sync: 'idle' | 'saving' | 'error';
  toast: { id: number; text: string } | null;
  levelUp: LevelUpInfo | null;
  notices: string[];
  update: (fn: Updater) => void;
  showToast: (text: string) => void;
  dismissLevelUp: () => void;
  dismissNotices: () => void;
  syncHealth: (opts?: { silent?: boolean }) => Promise<void>;
  resetProgress: () => void;
}

const Ctx = createContext<Store | null>(null);

export function useStore(): Store {
  const s = useContext(Ctx);
  if (!s) throw new Error('useStore must be used inside <StoreProvider>');
  return s;
}

/** For screens that only render once a game state is loaded. */
export function useGame(): Store & { game: GameState } {
  const s = useStore();
  if (!s.game) throw new Error('Game state not loaded');
  return s as Store & { game: GameState };
}

const haptic = (kind: 'success' | 'warning') => {
  if (Platform.OS === 'web') return;
  Haptics.notificationAsync(
    kind === 'success' ? Haptics.NotificationFeedbackType.Success : Haptics.NotificationFeedbackType.Warning,
  ).catch(() => {});
};

export function StoreProvider({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [user, setUser] = useState<AuthUser | null>(null);
  const [game, setGame] = useState<GameState | null>(null);
  const [sync, setSync] = useState<Store['sync']>('idle');
  const [toast, setToast] = useState<Store['toast']>(null);
  const [levelUp, setLevelUp] = useState<LevelUpInfo | null>(null);
  const [notices, setNotices] = useState<string[]>([]);
  const gameRef = useRef<GameState | null>(null);
  const loadedFor = useRef<string | null>(null);

  const showToast = useCallback((text: string) => setToast({ id: Date.now(), text }), []);

  const handleEvents = useCallback(
    (events: GameEvent[]) => {
      const msgs: string[] = [];
      for (const e of events) {
        switch (e.type) {
          case 'levelUp':
            setLevelUp({ lv: e.lv, rankUp: e.rankUp });
            haptic('success');
            break;
          case 'skillUp':
            showToast(`${skillDef(e.skill).name} → Lv ${e.lv}`);
            break;
          case 'cleared':
            showToast('Daily quests cleared! +50 XP');
            haptic('success');
            break;
          case 'raidCleared':
            showToast('Weekly raid cleared! +200 XP 🏆');
            haptic('success');
            break;
          case 'penaltyFailed':
            msgs.push(`You failed the Penalty Quest. ${e.xpLost} XP was lost.`);
            break;
          case 'shieldUsed':
            msgs.push(`A Streak Shield absorbed your missed quests on ${e.day}.`);
            break;
          case 'streakLost':
            msgs.push(`Your ${e.streak}-day streak has ended.`);
            break;
          case 'penaltyAssigned':
            msgs.push(
              `You did not complete yesterday's daily quests. A Penalty Quest has been issued — clear it today or lose ${penaltyLoss(gameRef.current?.lv ?? 1)} XP.`,
            );
            break;
        }
      }
      if (msgs.length) {
        setNotices((n) => [...n, ...msgs]);
        haptic('warning');
      }
    },
    [showToast],
  );

  const update = useCallback(
    (fn: Updater) => {
      const cur = gameRef.current;
      if (!cur) return;
      const out = fn(cur);
      const next = 'events' in out ? out.state : out;
      if ('events' in out) handleEvents(out.events);
      if (next === cur) return;
      gameRef.current = next;
      setGame(next);
      setSync('saving');
    },
    [handleEvents],
  );

  // ---- Auth + loading the signed-in user's state ----
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  const loadFor = useCallback(
    async (u: AuthUser | null) => {
      setUser(u);
      if (!u) {
        loadedFor.current = null;
        gameRef.current = null;
        setGame(null);
        setLoadError(null);
        return;
      }
      if (loadedFor.current === u.id) return;
      loadedFor.current = u.id;
      setLoading(true);
      setLoadError(null);
      try {
        const saved = await backend.loadState();
        const rolled = rollover(saved ?? newGameState(''));
        gameRef.current = rolled.state;
        setGame(rolled.state);
        handleEvents(rolled.events);
      } catch (e) {
        // Never fall back to a fresh state here: autosave would overwrite the real save.
        loadedFor.current = null;
        setLoadError((e as Error).message || 'Could not reach the server.');
      } finally {
        setLoading(false);
      }
    },
    [handleEvents],
  );

  useEffect(() => {
    let alive = true;
    backend
      .getUser()
      .then((u) => {
        if (alive) loadFor(u);
      })
      .finally(() => alive && setReady(true));
    const off = backend.onAuthChange((u) => loadFor(u));
    return () => {
      alive = false;
      off();
    };
  }, [loadFor]);

  // ---- Day rollover while the app stays open or returns to the foreground ----
  useEffect(() => {
    if (!game) return;
    const check = () => {
      if (gameRef.current && gameRef.current.day !== localDate()) update((s) => rollover(s));
    };
    const timer = setInterval(check, 30_000);
    const sub = AppState.addEventListener('change', (st) => st === 'active' && check());
    return () => {
      clearInterval(timer);
      sub.remove();
    };
  }, [!!game, update]); // eslint-disable-line react-hooks/exhaustive-deps

  // ---- Persist (debounced, flushed when the app backgrounds or the page closes) ----
  const pendingSave = useRef<GameState | null>(null);
  const flush = useCallback(() => {
    const s = pendingSave.current;
    if (!s) return;
    pendingSave.current = null;
    backend
      .saveState(s)
      .then(() => setSync((cur) => (pendingSave.current ? cur : 'idle')))
      .catch(() => {
        pendingSave.current ??= s;
        setSync('error');
      });
  }, []);

  useEffect(() => {
    if (!game || !user) return;
    pendingSave.current = game;
    const t = setTimeout(flush, 800);
    return () => clearTimeout(t);
  }, [game, user, flush]);

  useEffect(() => {
    const sub = AppState.addEventListener('change', (st) => st !== 'active' && flush());
    if (Platform.OS === 'web' && typeof window !== 'undefined') {
      window.addEventListener('pagehide', flush);
      return () => {
        sub.remove();
        window.removeEventListener('pagehide', flush);
      };
    }
    return () => sub.remove();
  }, [flush]);

  // ---- Reminders: reschedule when anything they depend on changes ----
  const reminderKey = game
    ? JSON.stringify([game.day, game.completed.length, game.quests.length, game.reminders])
    : '';
  useEffect(() => {
    if (!gameRef.current) return;
    scheduleReminders(gameRef.current).catch(() => {});
  }, [reminderKey]);

  // ---- Health sync ----
  const syncHealth = useCallback(
    async ({ silent = false } = {}) => {
      if (!healthSupported) return;
      try {
        const v = await readTodayVitals();
        update((s) => {
          // Keep whichever is higher so manual logs aren't wiped by a lower synced value.
          const merged: Partial<Vitals> = {};
          for (const k of Object.keys(v) as (keyof Vitals)[]) merged[k] = Math.max(s.vitals[k], v[k] ?? 0);
          return setVitals(s, merged);
        });
        if (!silent) showToast('Vitals synced ✓');
      } catch (e) {
        if (!silent) showToast(`Sync failed: ${(e as Error).message}`);
      }
    },
    [update, showToast],
  );

  useEffect(() => {
    if (!game?.healthSync) return;
    // Reads from the health store; state only updates after the async read resolves.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    syncHealth({ silent: true });
    const sub = AppState.addEventListener('change', (st) => st === 'active' && syncHealth({ silent: true }));
    return () => sub.remove();
  }, [game?.healthSync, game?.day, syncHealth]);

  const resetProgress = useCallback(() => {
    const cur = gameRef.current;
    if (!cur) return;
    const fresh = newGameState(cur.hunterName);
    gameRef.current = fresh;
    setGame(fresh);
    showToast('Progress reset');
  }, [showToast]);

  const value = useMemo<Store>(
    () => ({
      // Not ready until a signed-in user's game state has loaded.
      ready: ready && !loading && (!user || !!game || !!loadError),
      loadError,
      retryLoad: () => loadFor(user),
      user,
      game,
      pal: palette(game?.inventory.theme ?? 'violet'),
      sync,
      toast,
      levelUp,
      notices,
      update,
      showToast,
      dismissLevelUp: () => setLevelUp(null),
      dismissNotices: () => setNotices([]),
      syncHealth,
      resetProgress,
    }),
    [ready, loading, loadError, loadFor, user, game, sync, toast, levelUp, notices, update, showToast, syncHealth, resetProgress],
  );

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export const rankOf = (g: GameState) => getRank(g.lv);
