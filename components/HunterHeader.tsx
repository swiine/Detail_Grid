import { router } from 'expo-router';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { getRank, xpForLevel } from '@/lib/game';
import { C, F } from '@/lib/theme';
import { useGame } from '@/state/store';

import { Bar } from './ui';

export function HunterHeader() {
  const { game, pal, sync } = useGame();
  const rk = getRank(game.lv);
  const need = xpForLevel(game.lv);
  return (
    <View style={[styles.header, { borderBottomColor: pal.border2 }]}>
      <View style={styles.top}>
        <View>
          <Text style={[styles.title, { color: pal.p3 }]}>THE SOLO&apos;S ASCENT</Text>
          <Text style={[styles.subtitle, { color: pal.p2 }]}>HUNTER SYSTEM</Text>
        </View>
        <View style={styles.right}>
          <Text style={styles.gold}>🪙 {game.gold}</Text>
          <View style={[styles.rank, { borderColor: rk.color, shadowColor: rk.color }]}>
            <Text style={[styles.rankText, { color: rk.color }]}>RANK {rk.r}</Text>
          </View>
          <Pressable onPress={() => router.push('/settings')} hitSlop={10} accessibilityLabel="Settings">
            <Text style={styles.gear}>⚙️</Text>
          </Pressable>
        </View>
      </View>
      <View style={styles.xpRow}>
        <Text style={styles.xpText}>
          LV {game.lv} {sync === 'saving' ? '· saving…' : sync === 'error' ? '· offline, will retry' : ''}
        </Text>
        <Text style={styles.xpText}>
          {game.xp} / {need} XP
        </Text>
      </View>
      <Bar value={game.xp} max={need} style={{ marginTop: 3 }} />
      <View style={[styles.glow, { backgroundColor: pal.p, shadowColor: pal.p }]} />
    </View>
  );
}

const styles = StyleSheet.create({
  header: { backgroundColor: 'rgba(8,8,15,0.95)', borderBottomWidth: 1, paddingHorizontal: 16, paddingTop: 10, paddingBottom: 10 },
  top: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 },
  title: { fontFamily: F.titleBlack, fontSize: 15, letterSpacing: 2 },
  subtitle: { fontFamily: F.title, fontSize: 10, letterSpacing: 4, marginTop: 1 },
  right: { flexDirection: 'row', alignItems: 'center', gap: 10 },
  gold: { fontFamily: F.label, color: C.gold, fontSize: 13 },
  rank: { borderWidth: 1, borderRadius: 4, paddingHorizontal: 9, paddingVertical: 3, shadowOpacity: 0.5, shadowRadius: 6 },
  rankText: { fontFamily: F.label, fontSize: 12, letterSpacing: 1 },
  gear: { fontSize: 18 },
  xpRow: { flexDirection: 'row', justifyContent: 'space-between' },
  xpText: { fontFamily: F.labelSemi, fontSize: 11, color: C.text3, letterSpacing: 0.5 },
  glow: { position: 'absolute', bottom: -1, alignSelf: 'center', width: 200, height: 1, shadowOpacity: 0.8, shadowRadius: 8 },
});
