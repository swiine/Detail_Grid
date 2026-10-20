import { useEffect, useState } from 'react';
import { Animated, Easing, Modal, Pressable, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import { getRank } from '@/lib/game';
import { C, F } from '@/lib/theme';
import { useStore } from '@/state/store';

import { Button } from './ui';

export function Toast() {
  const { toast, pal } = useStore();
  const insets = useSafeAreaInsets();
  const anim = useState(() => new Animated.Value(0))[0];

  useEffect(() => {
    if (!toast) return;
    anim.setValue(0);
    Animated.sequence([
      Animated.timing(anim, { toValue: 1, duration: 180, useNativeDriver: true }),
      Animated.delay(1700),
      Animated.timing(anim, { toValue: 0, duration: 220, useNativeDriver: true }),
    ]).start();
  }, [toast, anim]);

  if (!toast) return null;
  return (
    <Animated.View
      pointerEvents="none"
      style={[
        styles.toast,
        {
          top: insets.top + 70,
          backgroundColor: pal.pm,
          borderColor: pal.p2,
          opacity: anim,
          transform: [{ translateY: anim.interpolate({ inputRange: [0, 1], outputRange: [-8, 0] }) }],
        },
      ]}>
      <Text style={styles.toastText}>{toast.text}</Text>
    </Animated.View>
  );
}

/** Full-screen Solo-Leveling-style "LEVEL UP" system window. */
export function LevelUpModal() {
  const { levelUp, dismissLevelUp, pal } = useStore();
  const scale = useState(() => new Animated.Value(0.6))[0];
  const fade = useState(() => new Animated.Value(0))[0];
  const spin = useState(() => new Animated.Value(0))[0];

  useEffect(() => {
    if (!levelUp) return;
    scale.setValue(0.6);
    fade.setValue(0);
    spin.setValue(0);
    Animated.parallel([
      Animated.spring(scale, { toValue: 1, friction: 5, useNativeDriver: true }),
      Animated.timing(fade, { toValue: 1, duration: 300, useNativeDriver: true }),
      Animated.loop(Animated.timing(spin, { toValue: 1, duration: 6000, easing: Easing.linear, useNativeDriver: true })),
    ]).start();
  }, [levelUp, scale, fade, spin]);

  if (!levelUp) return null;
  const rk = getRank(levelUp.lv);
  return (
    <Modal transparent animationType="fade" visible onRequestClose={dismissLevelUp}>
      <Pressable style={styles.backdrop} onPress={dismissLevelUp}>
        <Animated.View
          style={[
            styles.ring,
            {
              borderColor: (levelUp.rankUp ? rk.color : pal.p) + '66',
              transform: [{ rotate: spin.interpolate({ inputRange: [0, 1], outputRange: ['0deg', '360deg'] }) }],
            },
          ]}
        />
        <Animated.View style={[styles.window, { borderColor: pal.p, shadowColor: pal.p, opacity: fade, transform: [{ scale }] }]}>
          <Text style={[styles.sys, { color: pal.p2 }]}>[ SYSTEM ]</Text>
          <Text style={[styles.big, { color: pal.p3, textShadowColor: pal.p }]}>LEVEL UP!</Text>
          <Text style={styles.lv}>Level {levelUp.lv}</Text>
          {levelUp.rankUp && (
            <View style={[styles.rankBox, { borderColor: rk.color }]}>
              <Text style={[styles.rankUp, { color: rk.color }]}>RANK {rk.r}</Text>
              <Text style={styles.rankTitle}>{rk.title}</Text>
            </View>
          )}
          <Text style={styles.hint}>
            {levelUp.rankUp ? 'You have been re-awakened. New skills may now be unlocked.' : 'Your limits have been broken. Keep climbing, Hunter.'}
          </Text>
          <Button title="Continue" onPress={dismissLevelUp} style={{ alignSelf: 'stretch' }} />
        </Animated.View>
      </Pressable>
    </Modal>
  );
}

/** Rollover notices: penalty assigned, XP lost, shield used, streak broken. */
export function NoticeModal() {
  const { notices, dismissNotices, levelUp } = useStore();
  if (!notices.length || levelUp) return null;
  return (
    <Modal transparent animationType="fade" visible onRequestClose={dismissNotices}>
      <View style={styles.backdrop}>
        <View style={[styles.window, { borderColor: C.danger, shadowColor: C.danger }]}>
          <Text style={[styles.sys, { color: C.danger }]}>[ SYSTEM WARNING ]</Text>
          {notices.map((n, i) => (
            <Text key={i} style={styles.notice}>
              ◆ {n}
            </Text>
          ))}
          <Button title="Acknowledge" variant="danger" onPress={dismissNotices} style={{ alignSelf: 'stretch', marginTop: 14 }} />
        </View>
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  toast: { position: 'absolute', right: 14, paddingHorizontal: 12, paddingVertical: 6, borderRadius: 4, borderWidth: 1, zIndex: 50 },
  toastText: { color: '#fff', fontFamily: F.label, fontSize: 13, letterSpacing: 1 },
  backdrop: { flex: 1, backgroundColor: 'rgba(4,4,10,0.88)', alignItems: 'center', justifyContent: 'center', padding: 24 },
  ring: { position: 'absolute', width: 320, height: 320, borderRadius: 160, borderWidth: 1, borderStyle: 'dashed' },
  window: {
    width: '100%',
    maxWidth: 360,
    backgroundColor: C.bg2,
    borderWidth: 1,
    borderRadius: 12,
    padding: 22,
    alignItems: 'center',
    shadowOpacity: 0.7,
    shadowRadius: 24,
  },
  sys: { fontFamily: F.label, fontSize: 13, letterSpacing: 4, marginBottom: 10 },
  big: { fontFamily: F.titleBlack, fontSize: 34, letterSpacing: 3, textShadowRadius: 16 },
  lv: { fontFamily: F.label, fontSize: 20, color: C.text, marginTop: 6 },
  rankBox: { borderWidth: 1, borderRadius: 6, paddingHorizontal: 16, paddingVertical: 8, marginTop: 14, alignItems: 'center' },
  rankUp: { fontFamily: F.titleBlack, fontSize: 22, letterSpacing: 2 },
  rankTitle: { fontFamily: F.labelSemi, color: C.text2, fontSize: 13 },
  hint: { fontFamily: F.body, color: C.text2, fontSize: 13, textAlign: 'center', marginTop: 14, lineHeight: 19 },
  notice: { fontFamily: F.body, color: C.text, fontSize: 14, lineHeight: 21, alignSelf: 'stretch', marginTop: 6 },
});
