import {
  isHealthDataAvailable,
  queryCategorySamples,
  queryStatisticsForQuantity,
  requestAuthorization,
} from '@kingstinct/react-native-healthkit';

import type { Vitals } from './types';

export const healthSupported = isHealthDataAvailable();
export const healthSourceName = 'Apple Health';

const READ = [
  'HKQuantityTypeIdentifierStepCount',
  'HKQuantityTypeIdentifierActiveEnergyBurned',
  'HKQuantityTypeIdentifierDietaryWater',
  'HKCategoryTypeIdentifierSleepAnalysis',
] as const;

// HealthKit sleep values that count as actually asleep (not "in bed" or "awake").
const ASLEEP = new Set([1, 3, 4, 5]);

export async function requestHealthAccess(): Promise<boolean> {
  if (!healthSupported) return false;
  // iOS never reveals whether read access was granted; a "true" means the prompt was shown.
  return requestAuthorization({ toRead: READ });
}

export async function readTodayVitals(): Promise<Partial<Vitals>> {
  if (!healthSupported) return {};
  const now = new Date();
  const midnight = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const sleepFrom = new Date(midnight.getTime() - 6 * 3_600_000); // 6 PM yesterday
  const today = { filter: { date: { startDate: midnight, endDate: now } } };

  const [steps, energy, water, sleep] = await Promise.all([
    queryStatisticsForQuantity('HKQuantityTypeIdentifierStepCount', ['cumulativeSum'], { ...today, unit: 'count' }),
    queryStatisticsForQuantity('HKQuantityTypeIdentifierActiveEnergyBurned', ['cumulativeSum'], { ...today, unit: 'kcal' }),
    queryStatisticsForQuantity('HKQuantityTypeIdentifierDietaryWater', ['cumulativeSum'], { ...today, unit: 'L' }),
    queryCategorySamples('HKCategoryTypeIdentifierSleepAnalysis', {
      limit: 0,
      filter: { date: { startDate: sleepFrom, endDate: now } },
    }),
  ]);

  const sleepMs = sleep
    .filter((s) => ASLEEP.has(Number(s.value)))
    .reduce((sum, s) => sum + (new Date(s.endDate).getTime() - new Date(s.startDate).getTime()), 0);

  return {
    steps: Math.round(steps.sumQuantity?.quantity ?? 0),
    calories: Math.round(energy.sumQuantity?.quantity ?? 0),
    water: Math.round((water.sumQuantity?.quantity ?? 0) * 100) / 100,
    sleep: Math.round((sleepMs / 3_600_000) * 10) / 10,
  };
}
