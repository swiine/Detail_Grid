import { useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { HunterHeader } from '@/components/HunterHeader';
import { Bar, Button, Card, Field, Ring, Screen, Section, T } from '@/components/ui';
import { healthSourceName, healthSupported, requestHealthAccess } from '@/lib/health';
import { setVitals } from '@/lib/game';
import { C, F } from '@/lib/theme';
import type { VitalGoals, Vitals } from '@/lib/types';
import { useGame } from '@/state/store';

const METRICS: { key: keyof Vitals; label: string; short: string; unit: string; color: string; step: number }[] = [
  { key: 'steps', label: 'Steps', short: 'Steps', unit: '', color: '#9B59FF', step: 1000 },
  { key: 'calories', label: 'Active calories', short: 'Calories', unit: ' kcal', color: '#FFB800', step: 50 },
  { key: 'sleep', label: 'Sleep', short: 'Sleep', unit: 'h', color: '#00E5A0', step: 0.5 },
  { key: 'water', label: 'Water', short: 'Water', unit: 'L', color: '#4A9BF0', step: 0.25 },
];

const fmt = (k: keyof Vitals, v: number) => (k === 'steps' || k === 'calories' ? Math.round(v).toLocaleString() : String(Math.round(v * 100) / 100));

export default function VitalsScreen() {
  const { game, pal, update, syncHealth, showToast } = useGame();
  const v = game.vitals;
  const g = game.goals;
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState<Record<string, string>>({});
  const [syncing, setSyncing] = useState(false);

  const glasses = Math.round(g.water / 0.25);
  const filled = Math.min(glasses, Math.floor(v.water / 0.25 + 1e-6));

  const bump = (k: keyof Vitals, d: number) =>
    update((s) => setVitals(s, { [k]: Math.max(0, Math.round((s.vitals[k] + d) * 100) / 100) }));

  const startEdit = () => {
    const d: Record<string, string> = {};
    for (const m of METRICS) {
      d[m.key] = String(v[m.key]);
      d['goal_' + m.key] = String(g[m.key]);
    }
    setDraft(d);
    setEditing(true);
  };

  const saveEdit = () => {
    const vit: Partial<Vitals> = {};
    const goals: Partial<VitalGoals> = {};
    for (const m of METRICS) {
      const val = parseFloat(draft[m.key]);
      const goal = parseFloat(draft['goal_' + m.key]);
      if (!isNaN(val) && val >= 0) vit[m.key] = val;
      if (!isNaN(goal) && goal > 0) goals[m.key] = goal;
    }
    update((s) => setVitals({ ...s, goals: { ...s.goals, ...goals } }, vit));
    setEditing(false);
    showToast('Vitals updated');
  };

  const connect = async () => {
    setSyncing(true);
    const ok = await requestHealthAccess().catch(() => false);
    if (ok) {
      update((s) => ({ ...s, healthSync: true }));
      await syncHealth();
    } else showToast(`${healthSourceName} unavailable`);
    setSyncing(false);
  };

  return (
    <Screen header={<HunterHeader />}>
      <Card glow>
        <T kind="label" style={{ color: C.text2, letterSpacing: 1, marginBottom: 12 }}>
          TODAY&apos;S VITALS
        </T>
        <View style={styles.rings}>
          {METRICS.map((m) => (
            <Ring key={m.key} pct={(v[m.key] / g[m.key]) * 100} color={m.color} label={m.short} value={fmt(m.key, v[m.key]) + m.unit} size={58} />
          ))}
        </View>
      </Card>

      {healthSupported ? (
        game.healthSync ? (
          <Button
            title={`◈ Sync from ${healthSourceName}`}
            variant="ghost"
            loading={syncing}
            onPress={async () => {
              setSyncing(true);
              await syncHealth();
              setSyncing(false);
            }}
          />
        ) : (
          <Button title={`◈ Connect ${healthSourceName}`} variant="ghost" loading={syncing} onPress={connect} />
        )
      ) : (
        <Card>
          <T kind="muted">
            Automatic health sync works in the iOS (Apple Health) and Android (Health Connect) apps. Here you can log your vitals by hand.
          </T>
        </Card>
      )}

      <Section
        title="Progress"
        right={
          <Pressable onPress={editing ? saveEdit : startEdit} hitSlop={8}>
            <Text style={[styles.link, { color: pal.p }]}>{editing ? 'SAVE' : 'EDIT'}</Text>
          </Pressable>
        }
      />
      <Card>
        {METRICS.map((m) =>
          editing ? (
            <View key={m.key} style={styles.editRow}>
              <Field
                label={`${m.label}${m.unit ? ` (${m.unit.trim()})` : ''}`}
                value={draft[m.key]}
                onChangeText={(t) => setDraft((d) => ({ ...d, [m.key]: t }))}
                keyboardType="decimal-pad"
                containerStyle={{ flex: 2 }}
              />
              <Field
                label="Goal"
                value={draft['goal_' + m.key]}
                onChangeText={(t) => setDraft((d) => ({ ...d, ['goal_' + m.key]: t }))}
                keyboardType="decimal-pad"
                containerStyle={{ flex: 1 }}
              />
            </View>
          ) : (
            <View key={m.key} style={{ marginBottom: 12 }}>
              <View style={styles.srTop}>
                <T>{m.label}</T>
                <View style={styles.adjust}>
                  <Pressable onPress={() => bump(m.key, -m.step)} hitSlop={6} style={[styles.adj, { borderColor: pal.border2 }]}>
                    <Text style={styles.adjText}>−</Text>
                  </Pressable>
                  <Text style={styles.srVal}>
                    {fmt(m.key, v[m.key])} / {fmt(m.key, g[m.key])}
                    {m.unit}
                  </Text>
                  <Pressable onPress={() => bump(m.key, m.step)} hitSlop={6} style={[styles.adj, { borderColor: pal.border2 }]}>
                    <Text style={styles.adjText}>+</Text>
                  </Pressable>
                </View>
              </View>
              <Bar value={v[m.key]} max={g[m.key]} color={m.color} height={4} />
            </View>
          ),
        )}
        {editing && <Button title="Save" onPress={saveEdit} />}
      </Card>

      <Section title="Water log" />
      <Card>
        <View style={styles.wr}>
          <Text style={styles.wv}>{fmt('water', v.water)}L</Text>
          <T kind="muted">Goal: {g.water}L</T>
        </View>
        <View style={styles.glasses}>
          {Array.from({ length: glasses }).map((_, i) => (
            <View
              key={i}
              style={[styles.glass, { borderColor: pal.border, backgroundColor: i < filled ? 'rgba(74,155,240,0.55)' : pal.p + '10' }]}
            />
          ))}
        </View>
        <View style={{ flexDirection: 'row', gap: 8 }}>
          <Button title="− Remove" variant="ghost" style={{ flex: 1 }} onPress={() => bump('water', -0.25)} disabled={v.water <= 0} />
          <Button title="+ 250ml glass" variant="ghost" style={{ flex: 2 }} onPress={() => bump('water', 0.25)} />
        </View>
      </Card>
      <T kind="muted" style={{ marginTop: 6 }}>
        Quests marked ⚡ complete automatically when the matching vital reaches its goal.
      </T>
    </Screen>
  );
}

const styles = StyleSheet.create({
  rings: { flexDirection: 'row', justifyContent: 'space-around' },
  link: { fontFamily: F.label, fontSize: 12, letterSpacing: 1 },
  srTop: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  srVal: { fontFamily: F.labelSemi, fontSize: 13, color: C.text, minWidth: 100, textAlign: 'center' },
  adjust: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  adj: { width: 24, height: 24, borderRadius: 5, borderWidth: 1, alignItems: 'center', justifyContent: 'center' },
  adjText: { color: C.text2, fontSize: 15, lineHeight: 17 },
  editRow: { flexDirection: 'row', gap: 10 },
  wr: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 10 },
  wv: { fontFamily: F.label, fontSize: 28, color: C.text },
  glasses: { flexDirection: 'row', gap: 5, marginBottom: 4 },
  glass: { flex: 1, height: 30, borderRadius: 4, borderWidth: 1 },
});
