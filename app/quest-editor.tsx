import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Alert, Platform, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';

import { Button, Card, Chip, Field, Screen, Section, T } from '@/components/ui';
import { deleteQuest, isSkillUnlocked, QUEST_ICONS, SKILLS, uid, upsertQuest } from '@/lib/game';
import { C, F } from '@/lib/theme';
import type { Quest, QuestAuto, SkillId } from '@/lib/types';
import { useGame } from '@/state/store';

const XP_OPTIONS = [10, 20, 30, 40, 50, 75, 100];
const AUTO: { key: QuestAuto; label: string }[] = [
  { key: null, label: 'MANUAL' },
  { key: 'steps', label: 'STEPS GOAL' },
  { key: 'water', label: 'WATER GOAL' },
  { key: 'sleep', label: 'SLEEP GOAL' },
];

export default function QuestEditor() {
  const { id } = useLocalSearchParams<{ id?: string }>();
  const { game, pal, update, showToast } = useGame();
  const existing = game.quests.find((q) => q.id === id);
  const [name, setName] = useState(existing?.name ?? '');
  const [desc, setDesc] = useState(existing?.desc ?? '');
  const [icon, setIcon] = useState(existing?.icon ?? '⚔️');
  const [xp, setXp] = useState(existing?.xp ?? 30);
  const [skill, setSkill] = useState<SkillId>(existing?.skill ?? 'discipline');
  const [auto, setAuto] = useState<QuestAuto>(existing?.auto ?? null);
  const [err, setErr] = useState('');

  const close = () => (router.canGoBack() ? router.back() : router.replace('/'));

  const save = () => {
    if (name.trim().length < 2) return setErr('Give the quest a name.');
    const q: Quest = { id: existing?.id ?? uid(), name: name.trim(), desc: desc.trim(), icon, xp, skill, auto };
    update((s) => upsertQuest(s, q));
    showToast(existing ? 'Quest updated' : 'Quest added');
    close();
  };

  const remove = () => {
    if (!existing) return;
    const go = () => {
      update((s) => deleteQuest(s, existing.id));
      showToast('Quest deleted');
      close();
    };
    if (Platform.OS === 'web') {
      if (window.confirm(`Delete "${existing.name}"?`)) go();
    } else {
      Alert.alert('Delete quest', `Delete "${existing.name}"?`, [
        { text: 'Cancel', style: 'cancel' },
        { text: 'Delete', style: 'destructive', onPress: go },
      ]);
    }
  };

  return (
    <Screen>
      <View style={styles.top}>
        <Pressable onPress={close} hitSlop={10}>
          <Text style={[styles.topBtn, { color: C.text2 }]}>CANCEL</Text>
        </Pressable>
        <Text style={[styles.heading, { color: pal.p3 }]}>{existing ? 'Edit Quest' : 'New Quest'}</Text>
        <Pressable onPress={save} hitSlop={10}>
          <Text style={[styles.topBtn, { color: pal.p }]}>SAVE</Text>
        </Pressable>
      </View>

      <Card glow>
        <Field label="Quest name" value={name} onChangeText={(t) => { setName(t); setErr(''); }} placeholder="e.g. Meditate 10 minutes" maxLength={40} />
        <Field label="Description" value={desc} onChangeText={setDesc} placeholder="Optional details" maxLength={80} />
        {err ? <Text style={styles.err}>{err}</Text> : null}
      </Card>

      <Section title="Icon" />
      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{ gap: 6 }}>
        {QUEST_ICONS.map((i) => (
          <Pressable key={i} onPress={() => setIcon(i)} style={[styles.icon, { borderColor: icon === i ? pal.p : pal.border, backgroundColor: icon === i ? pal.pl : C.card }]}>
            <Text style={{ fontSize: 20 }}>{i}</Text>
          </Pressable>
        ))}
      </ScrollView>

      <Section title="XP reward" />
      <View style={styles.wrap}>
        {XP_OPTIONS.map((x) => (
          <Chip key={x} label={`${x} XP`} active={xp === x} onPress={() => setXp(x)} />
        ))}
      </View>

      <Section title="Trains skill" />
      <View style={styles.wrap}>
        {SKILLS.filter((s) => isSkillUnlocked(s.id, game.lv)).map((s) => (
          <Chip key={s.id} label={s.name.toUpperCase()} active={skill === s.id} onPress={() => setSkill(s.id)} />
        ))}
      </View>

      <Section title="Completion" />
      <View style={styles.wrap}>
        {AUTO.map((a) => (
          <Chip key={a.label} label={a.label} active={auto === a.key} onPress={() => setAuto(a.key)} />
        ))}
      </View>
      <T kind="muted" style={{ marginTop: 6 }}>
        Auto quests complete themselves when that vital reaches its goal on the Vitals tab. You can still tick them by hand.
      </T>

      <Button title={existing ? 'Save changes' : 'Add quest'} onPress={save} style={{ marginTop: 18 }} />
      {existing && <Button title="Delete quest" variant="danger" onPress={remove} />}
    </Screen>
  );
}

const styles = StyleSheet.create({
  top: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12, marginTop: 4 },
  topBtn: { fontFamily: F.label, fontSize: 13, letterSpacing: 1 },
  heading: { fontFamily: F.title, fontSize: 16, letterSpacing: 1 },
  err: { color: C.danger, fontFamily: F.bodySemi, fontSize: 12 },
  icon: { width: 44, height: 44, borderRadius: 8, borderWidth: 1, alignItems: 'center', justifyContent: 'center' },
  wrap: { flexDirection: 'row', flexWrap: 'wrap', gap: 7 },
});
