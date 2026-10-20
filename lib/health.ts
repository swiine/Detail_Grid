// Fallback for web (and any platform without a health store): manual logging only.
// Platform builds use health.ios.ts and health.android.ts instead.
import type { Vitals } from './types';

export const healthSupported = false;
export const healthSourceName = 'Manual';

export async function requestHealthAccess(): Promise<boolean> {
  return false;
}

export async function readTodayVitals(): Promise<Partial<Vitals>> {
  return {};
}
