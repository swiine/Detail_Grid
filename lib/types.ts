export type SkillId =
  | 'strength'
  | 'endurance'
  | 'focus'
  | 'discipline'
  | 'recovery'
  | 'mindset'
  | 'nutrition'
  | 'social'
  | 'finance'
  | 'creativity';

/** A quest can auto-complete from a health metric reaching its goal. */
export type QuestAuto = 'steps' | 'water' | 'sleep' | null;

export interface Quest {
  id: string;
  name: string;
  desc: string;
  icon: string;
  xp: number;
  skill: SkillId;
  auto: QuestAuto;
}

export interface SkillProgress {
  lv: number;
  xp: number;
}

export interface DayRecord {
  done: number;
  total: number;
  cleared: boolean;
  /** A streak shield absorbed this missed day. */
  shielded?: boolean;
  /** The day ended with an unfinished penalty quest. */
  penalized?: boolean;
}

export interface Vitals {
  steps: number;
  calories: number;
  sleep: number; // hours
  water: number; // litres
}

export interface VitalGoals {
  steps: number;
  calories: number;
  sleep: number;
  water: number;
}

export interface Reminders {
  enabled: boolean;
  morning: string; // "HH:MM"
  evening: string; // "HH:MM"
}

export interface GameState {
  version: 1;
  hunterName: string;
  lv: number;
  xp: number;
  totalXp: number;
  gold: number;
  streak: number;
  bestStreak: number;
  skills: Record<SkillId, SkillProgress>;
  quests: Quest[];
  /** Local date (YYYY-MM-DD) that `completed`, `vitals` and `penalty` belong to. */
  day: string;
  completed: string[];
  /** Today's penalty quest, if yesterday was failed. */
  penalty: { active: boolean; done: boolean } | null;
  history: Record<string, DayRecord>;
  raid: { days: number; lastLogged: string | null; clears: number };
  vitals: Vitals;
  goals: VitalGoals;
  healthSync: boolean;
  inventory: { owned: string[]; theme: string; frame: string; shields: number };
  reminders: Reminders;
}

export interface PublicProfile {
  id: string;
  hunterName: string;
  friendCode: string;
  lv: number;
  totalXp: number;
  streak: number;
  frame: string;
}

export interface FriendRequest {
  id: string;
  from: PublicProfile;
}
