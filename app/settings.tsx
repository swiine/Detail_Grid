import { router } from 'expo-router';
import { useState } from 'react';
import { Alert, Platform, Pressable, StyleSheet, Switch, Text, View } from 'react-native';

import { Button, Card, Field, Screen, Section, T } from '@/components/ui';
import { backend } from '@/lib/backend';
import { healthSourceName, healthSupported, requestHealthAccess } from '@/lib/health';
import { ensureNotificationPermission } from '@/lib/notifications';
import { C, F } from '@/lib/theme';
import { useGame } from '@/state/store';

const TIME_RE = /^([01]\d|2[0-3]):[0-5]\d$/;

export default function Settings() {
  const { game, user, pal, update, showToast, resetProgress, syncHealth } = useGame();
  const [name, setName] = useState(game.hunterName);
  const [morning, setMorning] = useState(game.reminders.morning);
  const [evening, setEvening] = useState(game.reminders.evening);
  const close = () => (router.canGoBack() ? router.back() : router.replace('/'));

  const confirm = (title: string, body: string, action: string, go: () => void) => {
    if (Platform.OS === 'web') {
      if (window.confirm(`${title}\n\n${body}`)) go();
    } else Alert.alert(title, body, [{ text: 'Cancel', style: 'cancel' }, { text: action, style: 'destructive', onPress: go }]);
  };

  const saveProfile = () => {
    const n = name.trim();
    if (n.length < 2 || n.length > 24) return showToast('Name must be 2–24 characters');
    update((s) => ({ ...s, hunterName: n }));
    showToast('Name saved');
  };

  const saveTimes = () => {
    if (!TIME_RE.test(morning) || !TIME_RE.test(evening)) return showToast('Use 24-hour HH:MM, e.g. 08:00');
    update((s) => ({ ...s, reminders: { ...s.reminders, morning, evening } }));
    showToast('Reminder times saved');
  };

  const toggleReminders = async (on: boolean) => {
    if (on && !(await ensureNotificationPermission())) return showToast('Notifications are blocked in system settings');
    update((s) => ({ ...s, reminders: { ...s.reminders, enabled: on } }));
  };

  const toggleHealth = async (on: boolean) => {
    if (on && !(await requestHealthAccess().catch(() => false))) return showToast(`${healthSourceName} unavailable`);
    update((s) => ({ ...s, healthSync: on }));
    if (on) syncHealth();
  };

  return (
    <Screen>
      <View style={styles.top}>
        <Text style={[styles.heading, { color: pal.p3 }]}>Settings</Text>
        <Pressable onPress={close} hitSlop={10}>
          <Text style={[styles.done, { color: pal.p }]}>DONE</Text>
        </Pressable>
      </View>

      <Section title="Hunter" />
      <Card>
        <Field label="Hunter name" value={name} onChangeText={setName} maxLength={24} />
        <Button title="Save name" variant="ghost" onPress={saveProfile} style={{ marginTop: 0 }} />
        <T kind="muted" style={{ marginTop: 10 }}>
          {backend.mode === 'demo' ? 'Demo mode — progress is saved on this device only.' : `Signed in as ${user?.email ?? 'Apple/Google account'}`}
        </T>
      </Card>

      <Section title="Reminders" />
      <Card>
        {Platform.OS === 'web' ? (
          <T kind="muted">Reminders are available in the iOS and Android apps.</T>
        ) : (
          <>
            <View style={styles.row}>
              <View style={{ flex: 1 }}>
                <T kind="label">Daily reminders</T>
                <T kind="muted">Morning quest alert + evening penalty warning if quests are unfinished.</T>
              </View>
              <Switch value={game.reminders.enabled} onValueChange={toggleReminders} trackColor={{ true: pal.pm, false: C.card2 }} thumbColor="#fff" />
            </View>
            <View style={{ flexDirection: 'row', gap: 10, marginTop: 10 }}>
              <Field label="Morning" value={morning} onChangeText={setMorning} placeholder="08:00" maxLength={5} containerStyle={{ flex: 1 }} />
              <Field label="Evening warning" value={evening} onChangeText={setEvening} placeholder="20:00" maxLength={5} containerStyle={{ flex: 1 }} />
            </View>
            <Button title="Save times" variant="ghost" onPress={saveTimes} style={{ marginTop: 0 }} />
          </>
        )}
      </Card>

      {healthSupported && (
        <>
          <Section title="Health" />
          <Card>
            <View style={styles.row}>
              <View style={{ flex: 1 }}>
                <T kind="label">Sync {healthSourceName}</T>
                <T kind="muted">Reads steps, active calories, sleep and water each time you open the app.</T>
              </View>
              <Switch value={game.healthSync} onValueChange={toggleHealth} trackColor={{ true: pal.pm, false: C.card2 }} thumbColor="#fff" />
            </View>
          </Card>
        </>
      )}

      <Section title="Account" />
      <Button title="Sign out" variant="ghost" onPress={() => backend.signOut()} />
      <Button
        title="Reset all progress"
        variant="danger"
        onPress={() =>
          confirm('Reset progress?', 'Your level, XP, gold, streaks and purchases will be wiped. Quests reset to defaults.', 'Reset', () => {
            resetProgress();
            close();
          })
        }
      />
      <T kind="muted" style={{ textAlign: 'center', marginTop: 16 }}>
        The Solo&apos;s Ascent · v1.0.0
      </T>
    </Screen>
  );
}

const styles = StyleSheet.create({
  top: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginTop: 4 },
  heading: { fontFamily: F.title, fontSize: 18, letterSpacing: 1 },
  done: { fontFamily: F.label, fontSize: 14, letterSpacing: 1 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12 },
});
