import { Cinzel_700Bold } from '@expo-google-fonts/cinzel/700Bold';
import { Cinzel_900Black } from '@expo-google-fonts/cinzel/900Black';
import { Exo2_400Regular } from '@expo-google-fonts/exo-2/400Regular';
import { Exo2_600SemiBold } from '@expo-google-fonts/exo-2/600SemiBold';
import { Rajdhani_600SemiBold } from '@expo-google-fonts/rajdhani/600SemiBold';
import { Rajdhani_700Bold } from '@expo-google-fonts/rajdhani/700Bold';
import { useFonts } from 'expo-font';
import { DarkTheme, Stack, ThemeProvider } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect } from 'react';
import { ActivityIndicator, View } from 'react-native';

import { LevelUpModal, NoticeModal, Toast } from '@/components/Overlays';
import { Button, T } from '@/components/ui';
import { backend } from '@/lib/backend';
import { C } from '@/lib/theme';
import { StoreProvider, useStore } from '@/state/store';

export {
  // Catch any errors thrown by the Layout component.
  ErrorBoundary,
} from 'expo-router';

export const unstable_settings = {
  anchor: '(tabs)',
};

SplashScreen.preventAutoHideAsync();

const theme = { ...DarkTheme, colors: { ...DarkTheme.colors, background: C.bg, card: C.bg, primary: '#9B59FF' } };

export default function RootLayout() {
  const [loaded, error] = useFonts({
    Cinzel_700Bold,
    Cinzel_900Black,
    Rajdhani_600SemiBold,
    Rajdhani_700Bold,
    Exo2_400Regular,
    Exo2_600SemiBold,
  });

  useEffect(() => {
    if (error) throw error;
  }, [error]);

  if (!loaded) return null;

  return (
    <ThemeProvider value={theme}>
      <StoreProvider>
        <StatusBar style="light" />
        <RootNav />
      </StoreProvider>
    </ThemeProvider>
  );
}

function RootNav() {
  const { ready, user, game, loadError, retryLoad } = useStore();

  useEffect(() => {
    if (ready) SplashScreen.hideAsync();
  }, [ready]);

  if (!ready) {
    return (
      <View style={{ flex: 1, backgroundColor: C.bg, alignItems: 'center', justifyContent: 'center' }}>
        <ActivityIndicator color="#9B59FF" />
      </View>
    );
  }

  if (user && loadError) {
    return (
      <View style={{ flex: 1, backgroundColor: C.bg, alignItems: 'center', justifyContent: 'center', padding: 24 }}>
        <T kind="title">[ SYSTEM ERROR ]</T>
        <T style={{ textAlign: 'center', marginTop: 10 }}>Could not load your hunter data.{'\n'}{loadError}</T>
        <Button title="Retry" onPress={retryLoad} style={{ alignSelf: 'stretch', marginTop: 18 }} />
        <Button title="Sign out" variant="ghost" onPress={() => backend.signOut()} style={{ alignSelf: 'stretch' }} />
      </View>
    );
  }

  const signedIn = !!user && !!game;
  const named = signedIn && !!game.hunterName;

  return (
    <>
      <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: C.bg } }}>
        <Stack.Protected guard={!user}>
          <Stack.Screen name="sign-in" />
        </Stack.Protected>
        <Stack.Protected guard={signedIn && !named}>
          <Stack.Screen name="onboarding" />
        </Stack.Protected>
        <Stack.Protected guard={named}>
          <Stack.Screen name="(tabs)" />
          <Stack.Screen name="quest-editor" options={{ presentation: 'modal' }} />
          <Stack.Screen name="settings" options={{ presentation: 'modal' }} />
        </Stack.Protected>
      </Stack>
      <Toast />
      <LevelUpModal />
      <NoticeModal />
    </>
  );
}
