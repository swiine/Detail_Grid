// Scheduled local notifications aren't supported on web.
import type { GameState } from './types';

export async function ensureNotificationPermission(): Promise<boolean> {
  return false;
}

export async function scheduleReminders(_s: GameState): Promise<void> {}
