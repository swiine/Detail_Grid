import { router } from 'expo-router';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { HunterHeader } from '@/components/HunterHeader';
import { Bar, Card, Screen, Section, T } from '@/components/ui';
import {
  CLEAR_BONUS,
  completePenalty,
  getRank,
  goldFor,
  isCleared,
  localDate,
  logRaid,
  PENALTY_QUEST,
  penaltyLoss,
  RAID_REWARD,
  skillDef,
  toggleQuest,
} from '@/lib/game';
import { C, F } from '@/lib/theme';
import type { Quest } from '@/lib/types';
import { useGame } from '@/state/store';

export default function QuestsScreen() {
  const { game, pal, update, showToast } = useGame();
  const done = game.quests.filter((q) => game.completed.includes(q.id)).length;
  const rk = getRank(game.lv);
  const cleared = isCleared(game);
  const raidToday = game.raid.lastLogged === localDate();

  const toggle = (q: Quest) => {
    const was = game.completed.includes(q.id);
    update((s) => toggleQuest(s, q.id));
    showToast(was ? `−${q.xp} XP` : `+${q.xp} XP · +${goldFor(q.xp)} 🪙`);
  };

  return (
    <Screen header={<HunterHeader />}>
      <Card glow>
        <Text style={[styles.greet, { color: pal.p3 }]}>⚔ Daily System Quests</Text>
        <T style={{ marginTop: 2 }}>
          {done}/{game.quests.length} complete — Rank {rk.r} · {rk.title}
        </T>
        <Bar value={done} max={game.quests.length || 1} />
        <View style={styles.statsRow}>
          <Stat label="Streak" value={`🔥 ${game.streak}`} />
          <Stat label="Best" value={`${game.bestStreak}`} />
          <Stat label="Shields" value={`🛡️ ${game.inventory.shields}`} />
        </View>
        {cleared && <Text style={[styles.clear, { color: C.teal }]}>✓ All quests cleared. Clear bonus +{CLEAR_BONUS.xp} XP earned.</Text>}
      </Card>

      {game.penalty?.active && (
        <>
          <Section title="Penalty quest" />
          <Pressable
            onPress={() => !game.penalty?.done && update(completePenalty)}
            style={[styles.quest, { borderColor: C.danger + (game.penalty.done ? '33' : '88'), backgroundColor: C.danger + '0D' }, game.penalty.done && styles.dim]}>
            <View style={[styles.icon, { borderColor: C.danger + '66', backgroundColor: C.danger + '14' }]}>
              <Text style={styles.emoji}>{PENALTY_QUEST.icon}</Text>
            </View>
            <View style={{ flex: 1 }}>
              <Text style={[styles.qname, { color: C.danger }, game.penalty.done && styles.strike]}>{PENALTY_QUEST.name}</Text>
              <T kind="muted">
                {game.penalty.done
                  ? 'Penalty survived. The System acknowledges your resolve.'
                  : `${PENALTY_QUEST.desc} (−${penaltyLoss(game.lv)} XP)`}
              </T>
            </View>
            <Check done={game.penalty.done} color={C.danger} />
          </Pressable>
        </>
      )}

      <Section
        title="Daily quests"
        right={
          <Pressable onPress={() => router.push('/quest-editor')} hitSlop={8}>
            <Text style={[styles.add, { color: pal.p }]}>+ NEW</Text>
          </Pressable>
        }
      />
      {game.quests.length === 0 && (
        <Card>
          <T>No quests yet. Tap + NEW to create one.</T>
        </Card>
      )}
      {game.quests.map((q) => {
        const dn = game.completed.includes(q.id);
        const sk = skillDef(q.skill);
        return (
          <Pressable
            key={q.id}
            onPress={() => toggle(q)}
            onLongPress={() => router.push({ pathname: '/quest-editor', params: { id: q.id } })}
            style={({ pressed }) => [styles.quest, { borderColor: pal.border }, dn && styles.dim, pressed && { opacity: 0.75 }]}>
            <View style={[styles.icon, { borderColor: sk.color + '55', backgroundColor: sk.color + '12' }]}>
              <Text style={styles.emoji}>{q.icon}</Text>
            </View>
            <View style={{ flex: 1 }}>
              <Text style={[styles.qname, dn && styles.strike]}>{q.name}</Text>
              <T kind="muted" numberOfLines={2}>
                <Text style={{ color: sk.color }}>+{q.xp} XP {sk.name}</Text>
                {q.auto ? ' · auto ⚡' : ''}
                {q.desc ? ` · ${q.desc}` : ''}
              </T>
            </View>
            <Check done={dn} color={pal.p} />
          </Pressable>
        );
      })}
      <T kind="muted" style={{ textAlign: 'center', marginTop: 2 }}>
        Tap to complete · long-press to edit
      </T>

      <Section title="Weekly raid" />
      <Pressable
        onPress={() => {
          if (raidToday) return showToast('Already logged today');
          update((s) => logRaid(s));
        }}
        style={[styles.quest, { borderColor: pal.border }, raidToday && styles.dim]}>
        <View style={[styles.icon, { borderColor: C.gold + '55', backgroundColor: C.gold + '12' }]}>
          <Text style={styles.emoji}>🏆</Text>
        </View>
        <View style={{ flex: 1 }}>
          <Text style={styles.qname}>7-Day No Junk Food Streak</Text>
          <T kind="muted">
            +{RAID_REWARD.xp} XP · +{RAID_REWARD.gold} 🪙 — log each clean day. Miss one and it restarts.
          </T>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: 8 }}>
            <Bar value={game.raid.days} max={7} color={C.gold} style={{ flex: 1 }} />
            <T kind="tiny" style={{ marginTop: 6 }}>
              Day {game.raid.days}/7
            </T>
          </View>
          {game.raid.clears > 0 && <T kind="tiny">Raids cleared: {game.raid.clears}</T>}
        </View>
        <Check done={raidToday} color={C.gold} />
      </Pressable>
    </Screen>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <View style={{ flex: 1, alignItems: 'center' }}>
      <Text style={{ fontFamily: F.label, fontSize: 16, color: C.text }}>{value}</Text>
      <T kind="tiny">{label.toUpperCase()}</T>
    </View>
  );
}

function Check({ done, color }: { done: boolean; color: string }) {
  const { pal } = useGame();
  return (
    <View style={[styles.check, { borderColor: done ? color : pal.border2, backgroundColor: done ? color : 'transparent' }]}>
      {done && <Text style={{ color: '#fff', fontSize: 12, fontWeight: '700' }}>✓</Text>}
    </View>
  );
}

const styles = StyleSheet.create({
  greet: { fontFamily: F.title, fontSize: 15, letterSpacing: 1 },
  statsRow: { flexDirection: 'row', marginTop: 12 },
  clear: { fontFamily: F.bodySemi, fontSize: 12, marginTop: 10 },
  add: { fontFamily: F.label, fontSize: 12, letterSpacing: 1 },
  quest: {
    backgroundColor: C.card,
    borderWidth: 1,
    borderRadius: 11,
    paddingVertical: 11,
    paddingHorizontal: 13,
    marginBottom: 7,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 11,
  },
  dim: { opacity: 0.45 },
  icon: { width: 40, height: 40, borderRadius: 8, borderWidth: 1, alignItems: 'center', justifyContent: 'center' },
  emoji: { fontSize: 18 },
  qname: { fontFamily: F.label, fontSize: 15, color: C.text, letterSpacing: 0.3 },
  strike: { textDecorationLine: 'line-through', color: C.text3 },
  check: { width: 24, height: 24, borderRadius: 5, borderWidth: 1, alignItems: 'center', justifyContent: 'center' },
});
