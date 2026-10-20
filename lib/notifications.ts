import * as Notifications from 'expo-notifications';
import { AndroidImportance } from 'expo-notifications';
import { Platform } from 'react-native';

import { addDays, isCleared, parseDate } from './game';
import type { GameState } from './types';

Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldShowBanner: true,
    shouldShowList: true,
    shouldPlaySound: true,
    shouldSetBadge: false,
  }),
});

const CHANNEL = 'system';
const WARNING_DAYS = 7;

export async function ensureNotificationPermission(): Promise<boolean> {
  if (Platform.OS === 'android') {
    await Notifications.setNotificationChannelAsync(CHANNEL, {
      name: 'System alerts',
      importance: AndroidImportance.HIGH,
    });
  }
  const current = await Notifications.getPermissionsAsync();
  if (current.granted) return true;
  if (!current.canAskAgain) return false;
  return (await Notifications.requestPermissionsAsync()).granted;
}

const hm = (t: string) => {
  const [hour, minute] = t.split(':').map(Number);
  return { hour, minute };
};

/**
 * Re-schedules all reminders from scratch: a repeating morning quest alert plus one-off
 * evening penalty warnings for the coming week. Today's warning is skipped once the
 * day's quests are cleared, so it's re-run whenever the state changes.
 */
export async function scheduleReminders(s: GameState): Promise<void> {
  await Notifications.cancelAllScheduledNotificationsAsync();
  if (!s.reminders.enabled) return;
  if (!(await Notifications.getPermissionsAsync()).granted) return;

  const morning = hm(s.reminders.morning);
  await Notifications.scheduleNotificationAsync({
    content: { title: '[SYSTEM] Daily Quest has arrived', body: 'Complete your daily quests, Hunter. Failure will result in a penalty.' },
    trigger: { type: Notifications.SchedulableTriggerInputTypes.DAILY, channelId: CHANNEL, ...morning },
  });

  const evening = hm(s.reminders.evening);
  const now = Date.now();
  for (let i = 0; i < WARNING_DAYS; i++) {
    if (i === 0 && isCleared(s)) continue;
    const date = parseDate(addDays(s.day, i));
    date.setHours(evening.hour, evening.minute, 0, 0);
    if (date.getTime() <= now) continue;
    await Notifications.scheduleNotificationAsync({
      content: {
        title: '[WARNING] Penalty Zone approaching',
        body: 'Your daily quests are unfinished. Clear them before midnight or face the penalty.',
      },
      trigger: { type: Notifications.SchedulableTriggerInputTypes.DATE, channelId: CHANNEL, date },
    });
  }
}
