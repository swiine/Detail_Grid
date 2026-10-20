import { LinearGradient } from 'expo-linear-gradient';
import type { ReactNode } from 'react';
import {
  ActivityIndicator,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View,
  type StyleProp,
  type TextInputProps,
  type TextStyle,
  type ViewStyle,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import Svg, { Circle, Defs, Line, Pattern, Rect } from 'react-native-svg';

import { FRAMES } from '@/lib/game';
import { C, F } from '@/lib/theme';
import { useStore } from '@/state/store';

/** Faint hex-grid backdrop from the original design. */
export function HexBackground() {
  const { pal } = useStore();
  return (
    <Svg style={StyleSheet.absoluteFill} pointerEvents="none">
      <Defs>
        <Pattern id="hex" width="32" height="28" patternUnits="userSpaceOnUse">
          <Line x1="0" y1="0" x2="16" y2="28" stroke={pal.p} strokeOpacity={0.05} strokeWidth={1} />
          <Line x1="32" y1="0" x2="16" y2="28" stroke={pal.p} strokeOpacity={0.05} strokeWidth={1} />
          <Line x1="0" y1="14" x2="32" y2="14" stroke={pal.p} strokeOpacity={0.035} strokeWidth={1} />
        </Pattern>
      </Defs>
      <Rect width="100%" height="100%" fill="url(#hex)" />
    </Svg>
  );
}

export function Screen({ children, header, scroll = true }: { children: ReactNode; header?: ReactNode; scroll?: boolean }) {
  return (
    <SafeAreaView style={styles.screen} edges={header ? ['top'] : ['top', 'bottom']}>
      <HexBackground />
      {header}
      {scroll ? (
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled" showsVerticalScrollIndicator={false}>
          {children}
        </ScrollView>
      ) : (
        <View style={[styles.content, { flex: 1 }]}>{children}</View>
      )}
    </SafeAreaView>
  );
}

export function Card({ children, style, glow, onPress }: { children: ReactNode; style?: StyleProp<ViewStyle>; glow?: boolean; onPress?: () => void }) {
  const { pal } = useStore();
  const body = (
    <>
      <LinearGradient
        colors={['transparent', glow ? pal.p : pal.border3, 'transparent']}
        start={{ x: 0, y: 0 }}
        end={{ x: 1, y: 0 }}
        style={styles.cardTopLine}
      />
      {children}
    </>
  );
  const s = [styles.card, { borderColor: glow ? pal.border2 : pal.border }, style];
  if (onPress)
    return (
      <Pressable onPress={onPress} style={({ pressed }) => [s, pressed && { opacity: 0.75 }]}>
        {body}
      </Pressable>
    );
  return <View style={s}>{body}</View>;
}

export function Section({ title, right }: { title: string; right?: ReactNode }) {
  const { pal } = useStore();
  return (
    <View style={styles.sec}>
      <Text style={[styles.secText, { color: pal.p }]}>{title.toUpperCase()}</Text>
      <View style={[styles.secLine, { backgroundColor: pal.border }]} />
      {right}
    </View>
  );
}

export function Bar({ value, max, color, height = 3, style }: { value: number; max: number; color?: string; height?: number; style?: StyleProp<ViewStyle> }) {
  const { pal } = useStore();
  const pct = max > 0 ? Math.min(100, Math.max(0, (value / max) * 100)) : 0;
  return (
    <View style={[{ height, backgroundColor: pal.p + '1F', borderRadius: 999, overflow: 'hidden', marginTop: 6 }, style]}>
      <View style={{ width: `${pct}%`, height: '100%', backgroundColor: color ?? pal.p, borderRadius: 999 }} />
    </View>
  );
}

export function Button({
  title,
  onPress,
  variant = 'primary',
  disabled,
  loading,
  style,
  icon,
}: {
  title: string;
  onPress: () => void;
  variant?: 'primary' | 'ghost' | 'danger' | 'light';
  disabled?: boolean;
  loading?: boolean;
  style?: StyleProp<ViewStyle>;
  icon?: ReactNode;
}) {
  const { pal } = useStore();
  const inner = (
    <View style={styles.btnInner}>
      {loading ? <ActivityIndicator color={variant === 'light' ? '#000' : '#fff'} size="small" /> : icon}
      <Text
        style={[
          styles.btnText,
          variant === 'ghost' && { color: pal.p2 },
          variant === 'danger' && { color: C.danger },
          variant === 'light' && { color: '#111' },
        ]}>
        {title.toUpperCase()}
      </Text>
    </View>
  );
  return (
    <Pressable
      onPress={onPress}
      disabled={disabled || loading}
      accessibilityRole="button"
      style={({ pressed }) => [
        styles.btn,
        variant === 'ghost' && { backgroundColor: pal.p + '1F', borderWidth: 1, borderColor: pal.border2 },
        variant === 'danger' && { backgroundColor: C.danger + '14', borderWidth: 1, borderColor: C.danger + '55' },
        variant === 'light' && { backgroundColor: '#fff' },
        (pressed || disabled) && { opacity: disabled ? 0.45 : 0.8 },
        style,
      ]}>
      {variant === 'primary' ? (
        <LinearGradient colors={[pal.pm, pal.pd]} start={{ x: 0, y: 0 }} end={{ x: 1, y: 1 }} style={styles.btnGrad}>
          {inner}
        </LinearGradient>
      ) : (
        <View style={styles.btnGrad}>{inner}</View>
      )}
    </Pressable>
  );
}

export function Field(props: TextInputProps & { label?: string; containerStyle?: StyleProp<ViewStyle> }) {
  const { pal } = useStore();
  const { label, style, containerStyle, ...rest } = props;
  return (
    <View style={[{ marginBottom: 10 }, containerStyle]}>
      {label && <Text style={styles.fieldLabel}>{label.toUpperCase()}</Text>}
      <TextInput
        placeholderTextColor={C.text3}
        {...rest}
        style={[styles.field, { borderColor: pal.border2 }, style]}
      />
    </View>
  );
}

export function Chip({ label, active, onPress }: { label: string; active?: boolean; onPress: () => void }) {
  const { pal } = useStore();
  return (
    <Pressable
      onPress={onPress}
      style={[styles.chip, { borderColor: active ? pal.p : pal.border, backgroundColor: active ? pal.pm : C.card }]}>
      <Text style={[styles.chipText, active && { color: '#fff' }]}>{label}</Text>
    </Pressable>
  );
}

export function Ring({ pct, color, label, value, size = 64 }: { pct: number; color: string; label: string; value: string; size?: number }) {
  const { pal } = useStore();
  const st = 5;
  const r = (size - st) / 2;
  const ci = 2 * Math.PI * r;
  const cl = Math.min(100, Math.max(0, Math.round(pct)));
  return (
    <View style={{ alignItems: 'center', gap: 4 }}>
      <View style={{ width: size, height: size, alignItems: 'center', justifyContent: 'center' }}>
        <Svg width={size} height={size} style={{ position: 'absolute', transform: [{ rotate: '-90deg' }] }}>
          <Circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke={pal.p + '18'} strokeWidth={st} />
          <Circle
            cx={size / 2}
            cy={size / 2}
            r={r}
            fill="none"
            stroke={color}
            strokeWidth={st}
            strokeDasharray={`${(cl / 100) * ci} ${ci}`}
            strokeLinecap="round"
          />
        </Svg>
        <Text style={{ color, fontFamily: F.label, fontSize: 12 }}>{cl}%</Text>
      </View>
      <Text style={{ color: C.text, fontFamily: F.labelSemi, fontSize: 12 }}>{value}</Text>
      <Text style={{ color: C.text3, fontFamily: F.labelSemi, fontSize: 11, letterSpacing: 0.5 }}>{label}</Text>
    </View>
  );
}

export function Avatar({ name, frame, size = 50, color }: { name: string; frame: string; size?: number; color?: string }) {
  const { pal } = useStore();
  const f = FRAMES[frame] ?? FRAMES.none;
  return (
    <View style={{ width: size + 8, height: size + 8, alignItems: 'center', justifyContent: 'center' }}>
      <View
        style={{
          width: size,
          height: size,
          borderRadius: size / 2,
          backgroundColor: pal.pl,
          borderWidth: 2,
          borderColor: frame === 'none' ? color ?? pal.border3 : f.color,
          alignItems: 'center',
          justifyContent: 'center',
          shadowColor: f.color,
          shadowOpacity: frame === 'none' ? 0 : 0.8,
          shadowRadius: 8,
        }}>
        <Text style={{ fontFamily: F.titleBlack, color: pal.p2, fontSize: size * 0.4 }}>{(name || 'H').charAt(0).toUpperCase()}</Text>
      </View>
      {f.emblem ? <Text style={{ position: 'absolute', right: -2, bottom: -2, fontSize: size * 0.32 }}>{f.emblem}</Text> : null}
    </View>
  );
}

export function T({ style, children, kind = 'body', numberOfLines }: { style?: StyleProp<TextStyle>; children: ReactNode; kind?: 'title' | 'label' | 'body' | 'muted' | 'tiny'; numberOfLines?: number }) {
  return (
    <Text numberOfLines={numberOfLines} style={[kindStyle[kind], style]}>
      {children}
    </Text>
  );
}

const kindStyle = StyleSheet.create({
  title: { fontFamily: F.title, color: C.text, fontSize: 15, letterSpacing: 1 },
  label: { fontFamily: F.label, color: C.text, fontSize: 14, letterSpacing: 0.3 },
  body: { fontFamily: F.body, color: C.text2, fontSize: 12, lineHeight: 18 },
  muted: { fontFamily: F.body, color: C.text3, fontSize: 11, lineHeight: 16 },
  tiny: { fontFamily: F.labelSemi, color: C.text3, fontSize: 11, letterSpacing: 0.5 },
});

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: C.bg },
  content: { padding: 14, paddingBottom: 40, width: '100%', maxWidth: 560, alignSelf: 'center' },
  card: { backgroundColor: C.card, borderWidth: 1, borderRadius: 12, padding: 13, marginBottom: 8, overflow: 'hidden' },
  cardTopLine: { position: 'absolute', top: 0, left: 0, right: 0, height: 1 },
  sec: { flexDirection: 'row', alignItems: 'center', gap: 8, marginTop: 16, marginBottom: 8 },
  secText: { fontFamily: F.label, fontSize: 11, letterSpacing: 2 },
  secLine: { flex: 1, height: 1 },
  btn: { borderRadius: 9, overflow: 'hidden', marginTop: 8 },
  btnGrad: { paddingVertical: 13, paddingHorizontal: 16, alignItems: 'center', justifyContent: 'center' },
  btnInner: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  btnText: { color: '#fff', fontFamily: F.label, fontSize: 13, letterSpacing: 1 },
  field: {
    backgroundColor: C.bg2,
    borderWidth: 1,
    borderRadius: 8,
    paddingHorizontal: 12,
    paddingVertical: 11,
    color: C.text,
    fontFamily: F.body,
    fontSize: 14,
  },
  fieldLabel: { fontFamily: F.label, color: C.text2, fontSize: 11, letterSpacing: 1, marginBottom: 5 },
  chip: { paddingHorizontal: 12, paddingVertical: 7, borderRadius: 4, borderWidth: 1 },
  chipText: { fontFamily: F.label, fontSize: 12, color: C.text2, letterSpacing: 0.5 },
});
