import {
  aggregateRecord,
  getSdkStatus,
  initialize,
  requestPermission,
  SdkAvailabilityStatus,
  type Permission,
} from 'react-native-health-connect';

import type { Vitals } from './types';

export const healthSupported = true;
export const healthSourceName = 'Health Connect';

const PERMISSIONS: Permission[] = [
  { accessType: 'read', recordType: 'Steps' },
  { accessType: 'read', recordType: 'ActiveCaloriesBurned' },
  { accessType: 'read', recordType: 'SleepSession' },
  { accessType: 'read', recordType: 'Hydration' },
];

async function ready(): Promise<boolean> {
  if ((await getSdkStatus()) !== SdkAvailabilityStatus.SDK_AVAILABLE) return false;
  return initialize();
}

export async function requestHealthAccess(): Promise<boolean> {
  if (!(await ready())) return false;
  const granted = await requestPermission(PERMISSIONS);
  return granted.length > 0;
}

export async function readTodayVitals(): Promise<Partial<Vitals>> {
  if (!(await ready())) return {};
  const now = new Date();
  const midnight = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const sleepFrom = new Date(midnight.getTime() - 6 * 3_600_000); // 6 PM yesterday
  const range = (from: Date) => ({ operator: 'between' as const, startTime: from.toISOString(), endTime: now.toISOString() });

  // Each read can fail on its own if that permission was denied.
  const safe = <T,>(p: Promise<T>) => p.catch(() => null);
  const [steps, cal, sleep, water] = await Promise.all([
    safe(aggregateRecord({ recordType: 'Steps', timeRangeFilter: range(midnight) })),
    safe(aggregateRecord({ recordType: 'ActiveCaloriesBurned', timeRangeFilter: range(midnight) })),
    safe(aggregateRecord({ recordType: 'SleepSession', timeRangeFilter: range(sleepFrom) })),
    safe(aggregateRecord({ recordType: 'Hydration', timeRangeFilter: range(midnight) })),
  ]);

  const out: Partial<Vitals> = {};
  if (steps) out.steps = Math.round(steps.COUNT_TOTAL);
  if (cal) out.calories = Math.round(cal.ACTIVE_CALORIES_TOTAL.inKilocalories);
  if (sleep) out.sleep = Math.round((sleep.SLEEP_DURATION_TOTAL / 3600) * 10) / 10;
  if (water) out.water = Math.round(water.VOLUME_TOTAL.inLiters * 100) / 100;
  return out;
}
