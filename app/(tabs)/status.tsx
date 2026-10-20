import { StyleSheet, Text, View } from 'react-native';

import { HunterHeader } from '@/components/HunterHeader';
import { Avatar, Bar, Card, Screen, Section, T } from '@/components/ui';
import {
  addDays,
  getRank,
  isSkillUnlocked,
  localDate,
  parseDate,
  RANKS,
  SKILLS,
  xpForLevel,
  xpForSkillLevel,
} from '@/lib/game';
import { C, F } from '@/lib/theme';
import { useGame } from '@/state/store';

const WEEKS = 15;

export default function StatusScreen() {
  const { game, pal } = useGame();
  const rk = getRank(game.lv);
  const need = xpForLevel(game.lv);
  const unlocked = SKILLS.filter((s) => isSkillUnlocked(s.id, game.lv));
  const locked = SKILLS.filter((s) => !isSkillUnlocked(s.id, game.lv));
  const totalSkillLv = unlocked.reduce((a, s) => a + game.skills[s.id].lv, 0);
  const days = Object.values(game.history);
  const clearedDays = days.filter((d) => d.cleared).length;
  const nextRank = RANKS.find((r) => r.min > game.lv);

  return (
    <Screen header={<HunterHeader />}>
      <Card glow>
        <View style={styles.hTop}>
          <Avatar name={game.hunterName} frame={game.inventory.frame} color={rk.color} />
          <View style={{ flex: 1 }}>
            <Text style={styles.hname}>{game.hunterName}</Text>
            <View style={[styles.pill, { borderColor: rk.color }]}>
              <Text style={[styles.pillText, { color: rk.color }]}>
                Rank {rk.r} — {rk.title}
              </Text>
            </View>
          </View>
        </View>
        <T kind="tiny">
          Level {game.lv} · {game.xp} / {need} XP
          {nextRank ? ` · Rank ${nextRank.r} at Lv ${nextRank.min}` : ' · Max rank'}
        </T>
        <Bar value={game.xp} max={need} height={4} />
      </Card>

      <Section title="Overview" />
      <View style={styles.grid}>
        <StatCard value={game.lv} label="Hunter level" />
        <StatCard value={totalSkillLv} label="Skill levels" />
        <StatCard value={game.totalXp.toLocaleString()} label="Total XP" />
        <StatCard value={clearedDays} label="Days cleared" />
        <StatCard value={`🔥 ${game.streak}`} label="Current streak" />
        <StatCard value={game.bestStreak} label="Best streak" />
      </View>

      <Section title="Quest calendar" />
      <Card>
        <Heatmap />
        <View style={styles.legend}>
          <Legend color={pal.p} label="Cleared" />
          <Legend color={pal.p + '55'} label="Partial" />
          <Legend color={C.blue} label="Shielded" />
          <Legend color={C.danger + '88'} label="Penalty" />
        </View>
      </Card>

      <Section title="Skills" />
      {unlocked.map((s) => {
        const sk = game.skills[s.id];
        const max = xpForSkillLevel(sk.lv);
        return (
          <Card key={s.id}>
            <View style={styles.sh}>
              <View style={{ flex: 1 }}>
                <T kind="label">{s.name}</T>
                <T kind="muted">{s.desc}</T>
              </View>
              <View style={[styles.lvl, { borderColor: pal.border2, backgroundColor: pal.p + '22' }]}>
                <Text style={[styles.lvlText, { color: pal.p2 }]}>Lv {sk.lv}</Text>
              </View>
            </View>
            <Bar value={sk.xp} max={max} color={s.color} height={4} />
            <T kind="tiny" style={{ marginTop: 4 }}>
              {sk.xp} / {max} XP
            </T>
          </Card>
        );
      })}

      {locked.length > 0 && (
        <>
          <Section title="Locked skills" />
          <T kind="muted" style={{ marginBottom: 8 }}>
            Advanced skills unlock as your rank rises. Assign quests to them once unlocked.
          </T>
          {locked.map((s) => (
            <Card key={s.id} style={{ opacity: 0.4 }}>
              <View style={styles.sh}>
                <View style={{ flex: 1 }}>
                  <T kind="label" style={{ color: C.text3 }}>
                    {s.name}
                  </T>
                  <T kind="muted">{s.desc}</T>
                </View>
                <T kind="tiny">🔒 Rank {s.unlock}</T>
              </View>
            </Card>
          ))}
        </>
      )}
    </Screen>
  );
}

function StatCard({ value, label }: { value: string | number; label: string }) {
  const { pal } = useGame();
  return (
    <View style={[styles.scard, { borderColor: pal.border }]}>
      <Text style={styles.sv}>{value}</Text>
      <T kind="tiny">{label}</T>
    </View>
  );
}

function Legend({ color, label }: { color: string; label: string }) {
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: 4 }}>
      <View style={{ width: 10, height: 10, borderRadius: 2, backgroundColor: color }} />
      <T kind="tiny">{label}</T>
    </View>
  );
}

function Heatmap() {
  const { game, pal } = useGame();
  const today = localDate();
  // Start on the Sunday WEEKS-1 weeks ago so columns are whole weeks.
  const start = addDays(today, -(7 * (WEEKS - 1)) - parseDate(today).getDay());
  const cols = Array.from({ length: WEEKS }, (_, w) => Array.from({ length: 7 }, (_, d) => addDays(start, w * 7 + d)));

  const colorFor = (day: string) => {
    if (day > today) return 'transparent';
    const live = day === today ? { done: game.completed.length, total: game.quests.length, cleared: game.history[today]?.cleared } : null;
    const rec = live ?? game.history[day];
    if (!rec) return C.card2;
    if (rec.cleared) return pal.p;
    if ('shielded' in rec && rec.shielded) return C.blue;
    if ('penalized' in rec && rec.penalized) return C.danger + '88';
    if (rec.done > 0) return pal.p + (rec.done / Math.max(1, rec.total) > 0.5 ? '88' : '44');
    return C.danger + '33';
  };

  return (
    <View style={{ flexDirection: 'row', gap: 3, justifyContent: 'center' }}>
      {cols.map((week, i) => (
        <View key={i} style={{ gap: 3, flex: 1, maxWidth: 18 }}>
          {week.map((day) => (
            <View
              key={day}
              style={{
                aspectRatio: 1,
                borderRadius: 3,
                backgroundColor: colorFor(day),
                borderWidth: day === today ? 1 : 0,
                borderColor: pal.p3,
              }}
            />
          ))}
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  hTop: { flexDirection: 'row', alignItems: 'center', gap: 12, marginBottom: 12 },
  hname: { fontFamily: F.title, fontSize: 17, color: C.text, letterSpacing: 1 },
  pill: { alignSelf: 'flex-start', borderWidth: 1, borderRadius: 3, paddingHorizontal: 8, paddingVertical: 2, marginTop: 4 },
  pillText: { fontFamily: F.label, fontSize: 11, letterSpacing: 1 },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: 8 },
  scard: { backgroundColor: C.bg2, borderWidth: 1, borderRadius: 9, padding: 10, flexBasis: '31%', flexGrow: 1 },
  sv: { fontFamily: F.label, fontSize: 20, color: C.text },
  legend: { flexDirection: 'row', flexWrap: 'wrap', gap: 12, justifyContent: 'center', marginTop: 12 },
  sh: { flexDirection: 'row', alignItems: 'flex-start', gap: 8 },
  lvl: { paddingHorizontal: 8, paddingVertical: 3, borderRadius: 3, borderWidth: 1 },
  lvlText: { fontFamily: F.label, fontSize: 12, letterSpacing: 0.5 },
});
