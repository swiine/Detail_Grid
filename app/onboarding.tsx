import { useState } from 'react';
import { Platform, StyleSheet, Switch, Text, View } from 'react-native';

import { Button, Card, Field, Screen, T } from '@/components/ui';
import { healthSourceName, healthSupported, requestHealthAccess } from '@/lib/health';
import { ensureNotificationPermission } from '@/lib/notifications';
import { C, F } from '@/lib/theme';
import { useGame } from '@/state/store';

export default function Onboarding() {
  const { game, user, pal, update, syncHealth } = useGame();
  const [name, setName] = useState(user?.suggestedName ?? '');
  const [reminders, setReminders] = useState(Platform.OS !== 'web');
  const [health, setHealth] = useState(healthSupported);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState('');

  const finish = async () => {
    const n = name.trim();
    if (n.length < 2) return setErr('Your hunter name needs at least 2 characters.');
    if (n.length > 24) return setErr('Keep it under 24 characters.');
    setBusy(true);
    const notif = reminders ? await ensureNotificationPermission().catch(() => false) : false;
    const healthOk = health ? await requestHealthAccess().catch(() => false) : false;
    update((s) => ({
      ...s,
      hunterName: n,
      healthSync: healthOk,
      reminders: { ...s.reminders, enabled: notif },
    }));
    if (healthOk) syncHealth({ silent: true });
    setBusy(false);
  };

  return (
    <Screen>
      <View style={styles.hero}>
        <Text style={[styles.sys, { color: pal.p2 }]}>[ SYSTEM ]</Text>
        <Text style={[styles.title, { color: pal.p3 }]}>You have acquired{'\n'}the qualifications{'\n'}to be a Player.</Text>
        <T style={{ textAlign: 'center', marginTop: 8 }}>Will you accept?</T>
      </View>

      <Card glow>
        <Field label="Hunter name" value={name} onChangeText={(t) => { setName(t); setErr(''); }} placeholder="e.g. Shadow Kai" maxLength={24} autoFocus />
        {err ? <Text style={styles.err}>{err}</Text> : null}
      </Card>

      {Platform.OS !== 'web' && (
        <Card>
          <Row
            title="Daily reminders"
            desc={`Quest alert at ${game.reminders.morning}, penalty warning at ${game.reminders.evening} if unfinished.`}
            value={reminders}
            onChange={setReminders}
          />
          {healthSupported && (
            <Row title={`Sync ${healthSourceName}`} desc="Auto-fill steps, sleep, calories and water. Some quests complete themselves." value={health} onChange={setHealth} />
          )}
        </Card>
      )}

      <Button title="Accept — Arise" loading={busy} onPress={finish} />
    </Screen>
  );
}

function Row({ title, desc, value, onChange }: { title: string; desc: string; value: boolean; onChange: (v: boolean) => void }) {
  const { pal } = useGame();
  return (
    <View style={styles.row}>
      <View style={{ flex: 1 }}>
        <T kind="label">{title}</T>
        <T kind="muted">{desc}</T>
      </View>
      <Switch value={value} onValueChange={onChange} trackColor={{ true: pal.pm, false: C.card2 }} thumbColor="#fff" />
    </View>
  );
}

const styles = StyleSheet.create({
  hero: { alignItems: 'center', paddingTop: 30, paddingBottom: 20 },
  sys: { fontFamily: F.label, letterSpacing: 4, fontSize: 13 },
  title: { fontFamily: F.title, fontSize: 20, letterSpacing: 1, textAlign: 'center', marginTop: 10, lineHeight: 30 },
  err: { color: C.danger, fontFamily: F.bodySemi, fontSize: 12 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingVertical: 8 },
});
