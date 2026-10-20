import * as AppleAuthentication from 'expo-apple-authentication';
import { useEffect, useState } from 'react';
import { Platform, StyleSheet, Text, View } from 'react-native';

import { Button, Card, Field, Screen, T } from '@/components/ui';
import { backend } from '@/lib/backend';
import { C, F } from '@/lib/theme';
import { useStore } from '@/state/store';

export default function SignIn() {
  const { pal } = useStore();
  const [mode, setMode] = useState<'in' | 'up'>('in');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState<string | null>(null);
  const [msg, setMsg] = useState<{ text: string; error?: boolean } | null>(null);
  const [appleNative, setAppleNative] = useState(false);
  const demo = backend.mode === 'demo';

  useEffect(() => {
    if (Platform.OS === 'ios') AppleAuthentication.isAvailableAsync().then(setAppleNative);
  }, []);

  const run = async (key: string, fn: () => Promise<void>) => {
    setBusy(key);
    setMsg(null);
    try {
      await fn();
    } catch (e) {
      const err = e as { code?: string; message?: string };
      if (err.code !== 'ERR_REQUEST_CANCELED') setMsg({ text: err.message ?? 'Something went wrong.', error: true });
    } finally {
      setBusy(null);
    }
  };

  const submitEmail = () =>
    run('email', async () => {
      if (!email.includes('@')) throw new Error('Enter a valid email.');
      if (password.length < 6) throw new Error('Password must be at least 6 characters.');
      if (mode === 'in') await backend.signInEmail(email.trim(), password);
      else if (await backend.signUpEmail(email.trim(), password))
        setMsg({ text: 'Check your inbox to confirm your email, then sign in.' });
    });

  return (
    <Screen>
      <View style={styles.hero}>
        <Text style={[styles.sys, { color: pal.p2 }]}>[ SYSTEM ]</Text>
        <Text style={[styles.title, { color: pal.p3, textShadowColor: pal.p }]}>THE SOLO&apos;S{'\n'}ASCENT</Text>
        <T style={{ textAlign: 'center', marginTop: 10 }}>
          You have been chosen as a Player.{'\n'}Complete daily quests. Level up. Break your limits.
        </T>
      </View>

      {demo && (
        <Card glow>
          <T kind="label" style={{ color: pal.p2 }}>
            ◈ Demo mode
          </T>
          <T style={{ marginTop: 4 }}>
            Cloud login isn&apos;t configured for this build, so progress is saved on this device only. Add Supabase keys to
            enable accounts — see the README.
          </T>
          <Button title="Enter as Guest" loading={busy === 'demo'} onPress={() => run('demo', backend.signInDemo)} />
        </Card>
      )}

      <Card style={demo && { opacity: 0.5 }}>
        <View style={styles.tabs}>
          {(['in', 'up'] as const).map((m) => (
            <Text
              key={m}
              onPress={() => setMode(m)}
              style={[styles.tab, mode === m && { color: pal.p, borderBottomColor: pal.p }]}>
              {m === 'in' ? 'SIGN IN' : 'CREATE ACCOUNT'}
            </Text>
          ))}
        </View>
        <Field label="Email" value={email} onChangeText={setEmail} autoCapitalize="none" keyboardType="email-address" autoComplete="email" placeholder="hunter@example.com" />
        <Field
          label="Password"
          value={password}
          onChangeText={setPassword}
          secureTextEntry
          autoComplete={mode === 'in' ? 'current-password' : 'new-password'}
          placeholder="••••••••"
          onSubmitEditing={submitEmail}
        />
        <Button title={mode === 'in' ? 'Sign In' : 'Awaken (Sign Up)'} loading={busy === 'email'} disabled={demo} onPress={submitEmail} />

        <View style={styles.or}>
          <View style={[styles.line, { backgroundColor: pal.border }]} />
          <T kind="tiny">OR</T>
          <View style={[styles.line, { backgroundColor: pal.border }]} />
        </View>

        <Button title="Continue with Google" variant="light" disabled={demo} loading={busy === 'google'} onPress={() => run('google', () => backend.signInOAuth('google'))} />
        {appleNative && !demo ? (
          <AppleAuthentication.AppleAuthenticationButton
            buttonType={AppleAuthentication.AppleAuthenticationButtonType.CONTINUE}
            buttonStyle={AppleAuthentication.AppleAuthenticationButtonStyle.WHITE}
            cornerRadius={9}
            style={{ height: 46, marginTop: 8 }}
            onPress={() => run('apple', () => backend.signInOAuth('apple'))}
          />
        ) : (
          <Button title="Continue with Apple" variant="light" disabled={demo} loading={busy === 'apple'} onPress={() => run('apple', () => backend.signInOAuth('apple'))} />
        )}
      </Card>

      {msg && <Text style={[styles.msg, { color: msg.error ? C.danger : pal.p2 }]}>{msg.text}</Text>}
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: { alignItems: 'center', paddingTop: 30, paddingBottom: 22 },
  sys: { fontFamily: F.label, letterSpacing: 4, fontSize: 13 },
  title: { fontFamily: F.titleBlack, fontSize: 32, letterSpacing: 3, textAlign: 'center', marginTop: 8, textShadowRadius: 18 },
  tabs: { flexDirection: 'row', gap: 18, marginBottom: 14 },
  tab: { fontFamily: F.label, fontSize: 13, color: C.text3, letterSpacing: 1, paddingBottom: 4, borderBottomWidth: 2, borderBottomColor: 'transparent' },
  or: { flexDirection: 'row', alignItems: 'center', gap: 10, marginVertical: 12 },
  line: { flex: 1, height: 1 },
  msg: { fontFamily: F.bodySemi, fontSize: 13, textAlign: 'center', marginTop: 10 },
});
