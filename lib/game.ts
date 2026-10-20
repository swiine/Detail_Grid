import type { DayRecord, GameState, Quest, SkillId } from './types';

// ---------- Dates (local time, YYYY-MM-DD) ----------

export function localDate(d: Date = new Date()): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

export function parseDate(s: string): Date {
  const [y, m, d] = s.split('-').map(Number);
  return new Date(y, m - 1, d);
}

export function addDays(s: string, n: number): string {
  const d = parseDate(s);
  d.setDate(d.getDate() + n);
  return localDate(d);
}

export function diffDays(from: string, to: string): number {
  return Math.round((parseDate(to).getTime() - parseDate(from).getTime()) / 86_400_000);
}

export function uid(): string {
  return Math.random().toString(36).slice(2, 10) + Date.now().toString(36).slice(-4);
}

// ---------- Levels & ranks ----------

export const xpForLevel = (lv: number) => 250 + 150 * lv;
export const xpForSkillLevel = (lv: number) => lv * 200;

export interface Rank {
  r: string;
  min: number;
  color: string;
  title: string;
}

export const RANKS: Rank[] = [
  { r: 'E', min: 1, color: '#9A98A8', title: 'Novice Hunter' },
  { r: 'D', min: 3, color: '#7CC23A', title: 'Apprentice Hunter' },
  { r: 'C', min: 6, color: '#1DBF8A', title: 'Skilled Hunter' },
  { r: 'B', min: 10, color: '#4A9BF0', title: 'Elite Hunter' },
  { r: 'A', min: 15, color: '#9B59FF', title: 'Expert Hunter' },
  { r: 'S', min: 21, color: '#FFB800', title: 'Master Hunter' },
  { r: 'SS', min: 28, color: '#FF4EF0', title: 'Legendary Hunter' },
  { r: 'SSS', min: 36, color: '#FF3355', title: 'Shadow Monarch' },
];

export function getRank(lv: number): Rank {
  let cur = RANKS[0];
  for (const r of RANKS) if (lv >= r.min) cur = r;
  return cur;
}

export const rankIndex = (r: string) => RANKS.findIndex((x) => x.r === r);

// ---------- Skills ----------

export interface SkillDef {
  id: SkillId;
  name: string;
  desc: string;
  color: string;
  /** Rank required to unlock; core skills are 'E'. */
  unlock: string;
}

export const SKILLS: SkillDef[] = [
  { id: 'strength', name: 'Strength', desc: 'Physical power & muscle mass', color: '#FFB800', unlock: 'E' },
  { id: 'endurance', name: 'Endurance', desc: 'Cardiovascular fitness & stamina', color: '#00E5A0', unlock: 'E' },
  { id: 'focus', name: 'Focus', desc: 'Mental clarity & deep work capacity', color: '#9B59FF', unlock: 'E' },
  { id: 'discipline', name: 'Discipline', desc: 'Consistency & willpower', color: '#4A9BF0', unlock: 'E' },
  { id: 'recovery', name: 'Recovery', desc: 'Sleep quality & stress management', color: '#FF4EF0', unlock: 'E' },
  { id: 'mindset', name: 'Mindset', desc: 'Mental resilience & growth mindset', color: '#C084FC', unlock: 'D' },
  { id: 'nutrition', name: 'Nutrition', desc: 'Diet quality & macro tracking', color: '#7CC23A', unlock: 'C' },
  { id: 'social', name: 'Social', desc: 'Communication & relationships', color: '#00F5FF', unlock: 'B' },
  { id: 'finance', name: 'Finance', desc: 'Money management & wealth-building', color: '#FFD966', unlock: 'A' },
  { id: 'creativity', name: 'Creativity', desc: 'Art, music, writing & building', color: '#FF7A59', unlock: 'S' },
];

export const skillDef = (id: SkillId) => SKILLS.find((s) => s.id === id)!;

export function isSkillUnlocked(id: SkillId, lv: number): boolean {
  return rankIndex(getRank(lv).r) >= rankIndex(skillDef(id).unlock);
}

// ---------- Quests ----------

export const DEFAULT_QUESTS: Quest[] = [
  { id: 'pushups', name: '100 Push-Ups', desc: 'Complete 100 push-ups in sets.', icon: '💪', xp: 50, skill: 'strength', auto: null },
  { id: 'steps', name: '10,000 Steps', desc: 'Hit your daily step goal.', icon: '🏃', xp: 40, skill: 'endurance', auto: 'steps' },
  { id: 'reading', name: 'Read 20 Minutes', desc: 'Study or read to sharpen focus.', icon: '📚', xp: 30, skill: 'focus', auto: null },
  { id: 'water', name: 'Drink 2L Water', desc: 'Stay hydrated — log each glass.', icon: '💧', xp: 20, skill: 'discipline', auto: 'water' },
  { id: 'coldshower', name: 'Cold Shower / Breathwork', desc: '2 min cold or 10 min breathwork.', icon: '❄️', xp: 35, skill: 'recovery', auto: null },
  { id: 'meals', name: 'Log Your Meals', desc: 'Track every meal today.', icon: '🍽️', xp: 25, skill: 'discipline', auto: null },
];

export const QUEST_ICONS = ['💪', '🏃', '📚', '💧', '❄️', '🍽️', '🧘', '😴', '🚴', '🏋️', '🧠', '✍️', '💰', '🎸', '🎨', '🤝', '🥗', '🚭', '📵', '🌅', '🧹', '💻', '🗣️', '🏊'];

export const PENALTY_QUEST = {
  name: 'PENALTY: Survive the Zone',
  desc: '50 push-ups, 50 squats and a 2 km run. Fail and lose XP at midnight.',
  icon: '☠️',
  xp: 30,
};

export const CLEAR_BONUS = { xp: 50, gold: 25 };
export const RAID_REWARD = { xp: 200, gold: 60 };
export const goldFor = (xp: number) => Math.max(1, Math.round(xp / 5));
export const penaltyLoss = (lv: number) => 50 + 25 * lv;

// ---------- Shop ----------

export type ShopKind = 'theme' | 'frame' | 'shield';

export interface ShopItem {
  id: string;
  kind: ShopKind;
  name: string;
  desc: string;
  icon: string;
  cost: number;
}

export const SHOP: ShopItem[] = [
  { id: 'theme:violet', kind: 'theme', name: 'Monarch Violet', desc: 'The System default.', icon: '🟣', cost: 0 },
  { id: 'theme:crimson', kind: 'theme', name: 'Red Gate Crimson', desc: 'Blood-red dungeon glow.', icon: '🔴', cost: 150 },
  { id: 'theme:azure', kind: 'theme', name: 'Frost Azure', desc: 'Ice-elf blue.', icon: '🔵', cost: 150 },
  { id: 'theme:emerald', kind: 'theme', name: 'Healer Emerald', desc: 'Calm, verdant mana.', icon: '🟢', cost: 200 },
  { id: 'theme:gold', kind: 'theme', name: 'S-Rank Gold', desc: 'For the national-level elite.', icon: '🟡', cost: 400 },
  { id: 'frame:none', kind: 'frame', name: 'Plain Ring', desc: 'No frame.', icon: '⭕', cost: 0 },
  { id: 'frame:flame', kind: 'frame', name: 'Ember Frame', desc: 'Burning orange aura.', icon: '🔥', cost: 120 },
  { id: 'frame:frost', kind: 'frame', name: 'Frost Frame', desc: 'Crystal ice border.', icon: '❄️', cost: 120 },
  { id: 'frame:shadow', kind: 'frame', name: 'Shadow Frame', desc: 'Arise. Dark smoke aura.', icon: '🌑', cost: 250 },
  { id: 'frame:crown', kind: 'frame', name: 'Monarch Crown', desc: 'Only for rulers.', icon: '👑', cost: 600 },
  { id: 'shield', kind: 'shield', name: 'Streak Shield', desc: 'Auto-protects your streak and blocks the penalty for one missed day. Hold up to 3.', icon: '🛡️', cost: 120 },
];

export const MAX_SHIELDS = 3;

export const FRAMES: Record<string, { color: string; emblem: string }> = {
  none: { color: 'transparent', emblem: '' },
  flame: { color: '#FF7A1A', emblem: '🔥' },
  frost: { color: '#7FDBFF', emblem: '❄️' },
  shadow: { color: '#3A2A6A', emblem: '🌑' },
  crown: { color: '#FFB800', emblem: '👑' },
};

// ---------- State ----------

export function newGameState(hunterName = ''): GameState {
  const skills = {} as GameState['skills'];
  for (const s of SKILLS) skills[s.id] = { lv: 1, xp: 0 };
  return {
    version: 1,
    hunterName,
    lv: 1,
    xp: 0,
    totalXp: 0,
    gold: 0,
    streak: 0,
    bestStreak: 0,
    skills,
    quests: DEFAULT_QUESTS.map((q) => ({ ...q })),
    day: localDate(),
    completed: [],
    penalty: null,
    history: {},
    raid: { days: 0, lastLogged: null, clears: 0 },
    vitals: { steps: 0, calories: 0, sleep: 0, water: 0 },
    goals: { steps: 10000, calories: 500, sleep: 8, water: 2 },
    healthSync: false,
    inventory: { owned: ['theme:violet', 'frame:none'], theme: 'violet', frame: 'none', shields: 0 },
    reminders: { enabled: true, morning: '08:00', evening: '20:00' },
  };
}

/** Fill in any fields missing from older saved states. */
export function migrate(raw: Partial<GameState> | null | undefined): GameState {
  const base = newGameState();
  if (!raw) return base;
  return {
    ...base,
    ...raw,
    skills: { ...base.skills, ...(raw.skills ?? {}) },
    raid: { ...base.raid, ...(raw.raid ?? {}) },
    vitals: { ...base.vitals, ...(raw.vitals ?? {}) },
    goals: { ...base.goals, ...(raw.goals ?? {}) },
    inventory: { ...base.inventory, ...(raw.inventory ?? {}) },
    reminders: { ...base.reminders, ...(raw.reminders ?? {}) },
    history: raw.history ?? {},
    version: 1,
  };
}

export type GameEvent =
  | { type: 'levelUp'; lv: number; rankUp: boolean }
  | { type: 'skillUp'; skill: SkillId; lv: number }
  | { type: 'cleared' }
  | { type: 'raidCleared' }
  | { type: 'penaltyAssigned' }
  | { type: 'penaltyFailed'; xpLost: number }
  | { type: 'shieldUsed'; day: string }
  | { type: 'streakLost'; streak: number };

type Result = { state: GameState; events: GameEvent[] };

const clone = (s: GameState): GameState => JSON.parse(JSON.stringify(s));

export function isCleared(s: GameState): boolean {
  return s.quests.length > 0 && s.quests.every((q) => s.completed.includes(q.id));
}

/** Mutates `s`. Adds XP to hunter (and skill), handling level ups. */
function grantXp(s: GameState, amt: number, skill: SkillId | null, events: GameEvent[]) {
  s.xp += amt;
  s.totalXp += amt;
  const startRank = getRank(s.lv).r;
  while (s.xp >= xpForLevel(s.lv)) {
    s.xp -= xpForLevel(s.lv);
    s.lv++;
    events.push({ type: 'levelUp', lv: s.lv, rankUp: getRank(s.lv).r !== startRank });
  }
  if (skill) {
    const sk = s.skills[skill];
    sk.xp += amt;
    while (sk.xp >= xpForSkillLevel(sk.lv)) {
      sk.xp -= xpForSkillLevel(sk.lv);
      sk.lv++;
      events.push({ type: 'skillUp', skill, lv: sk.lv });
    }
  }
}

/** Mutates `s`. Removes XP without ever dropping a level. */
function removeXp(s: GameState, amt: number, skill: SkillId | null) {
  const lost = Math.min(s.xp, amt);
  s.xp -= lost;
  s.totalXp = Math.max(0, s.totalXp - lost);
  if (skill) s.skills[skill].xp = Math.max(0, s.skills[skill].xp - amt);
  return lost;
}

function recordToday(s: GameState) {
  s.history[s.day] = {
    ...(s.history[s.day] ?? {}),
    done: s.quests.filter((q) => s.completed.includes(q.id)).length,
    total: s.quests.length,
    cleared: isCleared(s),
  };
}

/** Mutates `s`; grants or revokes the daily clear bonus and streak when clear status flips. */
function syncClear(s: GameState, wasCleared: boolean, events: GameEvent[]) {
  const nowCleared = isCleared(s);
  if (!wasCleared && nowCleared) {
    grantXp(s, CLEAR_BONUS.xp, 'discipline', events);
    s.gold += CLEAR_BONUS.gold;
    s.streak++;
    s.bestStreak = Math.max(s.bestStreak, s.streak);
    events.push({ type: 'cleared' });
  } else if (wasCleared && !nowCleared) {
    removeXp(s, CLEAR_BONUS.xp, 'discipline');
    s.gold = Math.max(0, s.gold - CLEAR_BONUS.gold);
    s.streak = Math.max(0, s.streak - 1);
  }
  recordToday(s);
}

/** Mutates `s`; awards the quest and the daily clear bonus if it completes the day. */
function complete(s: GameState, q: Quest, events: GameEvent[]) {
  if (s.completed.includes(q.id)) return;
  const wasCleared = isCleared(s);
  s.completed.push(q.id);
  grantXp(s, q.xp, q.skill, events);
  s.gold += goldFor(q.xp);
  syncClear(s, wasCleared, events);
}

export function toggleQuest(state: GameState, id: string): Result {
  const s = clone(state);
  const events: GameEvent[] = [];
  const q = s.quests.find((x) => x.id === id);
  if (!q) return { state, events };
  if (s.completed.includes(id)) {
    const wasCleared = isCleared(s);
    s.completed = s.completed.filter((x) => x !== id);
    removeXp(s, q.xp, q.skill);
    s.gold = Math.max(0, s.gold - goldFor(q.xp));
    syncClear(s, wasCleared, events);
  } else {
    complete(s, q, events);
  }
  return { state: s, events };
}

export function completePenalty(state: GameState): Result {
  if (!state.penalty?.active || state.penalty.done) return { state, events: [] };
  const s = clone(state);
  const events: GameEvent[] = [];
  s.penalty = { active: true, done: true };
  grantXp(s, PENALTY_QUEST.xp, 'discipline', events);
  return { state: s, events };
}

/** Auto-complete quests linked to a health metric that has reached its goal. */
export function applyAutoQuests(state: GameState): Result {
  const s = clone(state);
  const events: GameEvent[] = [];
  for (const q of s.quests) {
    if (!q.auto || s.completed.includes(q.id)) continue;
    if (s.vitals[q.auto] >= s.goals[q.auto]) complete(s, q, events);
  }
  return { state: s, events };
}

export function setVitals(state: GameState, patch: Partial<GameState['vitals']>): Result {
  const s = clone(state);
  s.vitals = { ...s.vitals, ...patch };
  return applyAutoQuests(s);
}

export function logRaid(state: GameState, today = localDate()): Result {
  if (state.raid.lastLogged === today) return { state, events: [] };
  const s = clone(state);
  const events: GameEvent[] = [];
  const consecutive = s.raid.lastLogged === addDays(today, -1);
  s.raid.days = consecutive ? s.raid.days + 1 : 1;
  s.raid.lastLogged = today;
  if (s.raid.days >= 7) {
    grantXp(s, RAID_REWARD.xp, 'discipline', events);
    s.gold += RAID_REWARD.gold;
    s.raid.days = 0;
    s.raid.clears++;
    events.push({ type: 'raidCleared' });
  }
  return { state: s, events };
}

/**
 * Close out every day between `state.day` and `today`: record history, resolve the
 * penalty quest, consume shields or break the streak, and assign a new penalty.
 */
export function rollover(state: GameState, today = localDate()): Result {
  if (state.day === today || diffDays(state.day, today) < 0) return { state, events: [] };
  const s = clone(state);
  const events: GameEvent[] = [];
  let assignPenalty = false;

  const missDay = (day: string, rec: DayRecord) => {
    if (s.inventory.shields > 0) {
      s.inventory.shields--;
      rec.shielded = true;
      events.push({ type: 'shieldUsed', day });
    } else {
      if (s.streak > 0) events.push({ type: 'streakLost', streak: s.streak });
      s.streak = 0;
      assignPenalty = true;
    }
  };

  // The last day the app saw.
  const rec: DayRecord = {
    done: s.quests.filter((q) => s.completed.includes(q.id)).length,
    total: s.quests.length,
    cleared: isCleared(s),
  };
  if (s.penalty?.active && !s.penalty.done) {
    const lost = removeXp(s, penaltyLoss(s.lv), null);
    rec.penalized = true;
    events.push({ type: 'penaltyFailed', xpLost: lost });
  }
  if (!rec.cleared) missDay(s.day, rec);
  s.history[s.day] = rec;

  // Days the app was never opened.
  for (let d = addDays(s.day, 1); d !== today; d = addDays(d, 1)) {
    const gap: DayRecord = { done: 0, total: s.quests.length, cleared: false };
    missDay(d, gap);
    s.history[d] = gap;
  }

  s.day = today;
  s.completed = [];
  s.vitals = { steps: 0, calories: 0, sleep: 0, water: 0 };
  s.penalty = assignPenalty ? { active: true, done: false } : null;
  if (assignPenalty) events.push({ type: 'penaltyAssigned' });
  return { state: s, events };
}

export type BuyResult = { ok: true; state: GameState } | { ok: false; reason: string };

export function buyItem(state: GameState, itemId: string): BuyResult {
  const item = SHOP.find((i) => i.id === itemId);
  if (!item) return { ok: false, reason: 'Unknown item.' };
  if (item.kind !== 'shield' && state.inventory.owned.includes(item.id))
    return { ok: false, reason: 'Already owned.' };
  if (item.kind === 'shield' && state.inventory.shields >= MAX_SHIELDS)
    return { ok: false, reason: `You can hold at most ${MAX_SHIELDS} shields.` };
  if (state.gold < item.cost) return { ok: false, reason: `Not enough gold. Need ${item.cost - state.gold} more.` };
  const s = clone(state);
  s.gold -= item.cost;
  if (item.kind === 'shield') s.inventory.shields++;
  else {
    s.inventory.owned.push(item.id);
    if (item.kind === 'theme') s.inventory.theme = item.id.split(':')[1];
    else s.inventory.frame = item.id.split(':')[1];
  }
  return { ok: true, state: s };
}

export function equipItem(state: GameState, itemId: string): GameState {
  if (!state.inventory.owned.includes(itemId)) return state;
  const [kind, key] = itemId.split(':');
  const s = clone(state);
  if (kind === 'theme') s.inventory.theme = key;
  if (kind === 'frame') s.inventory.frame = key;
  return s;
}

export function upsertQuest(state: GameState, q: Quest): Result {
  const s = clone(state);
  const events: GameEvent[] = [];
  const wasCleared = isCleared(s);
  const i = s.quests.findIndex((x) => x.id === q.id);
  if (i >= 0) s.quests[i] = q;
  else s.quests.push(q);
  syncClear(s, wasCleared, events);
  return { state: s, events };
}

export function deleteQuest(state: GameState, id: string): Result {
  const s = clone(state);
  const events: GameEvent[] = [];
  const wasCleared = isCleared(s);
  s.quests = s.quests.filter((q) => q.id !== id);
  s.completed = s.completed.filter((x) => x !== id);
  syncClear(s, wasCleared, events);
  return { state: s, events };
}
