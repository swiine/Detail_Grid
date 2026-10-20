import { Tabs } from 'expo-router';
import { Text } from 'react-native';

import { C, F } from '@/lib/theme';
import { useStore } from '@/state/store';

const icon = (emoji: string) =>
  function TabIcon({ focused }: { focused: boolean }) {
    return <Text style={{ fontSize: 18, opacity: focused ? 1 : 0.55 }}>{emoji}</Text>;
  };

export default function TabLayout() {
  const { pal } = useStore();
  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: pal.p,
        tabBarInactiveTintColor: C.text3,
        tabBarStyle: { backgroundColor: 'rgba(8,8,15,0.97)', borderTopColor: pal.border2 },
        tabBarLabelStyle: { fontFamily: F.label, fontSize: 10, letterSpacing: 0.5 },
      }}>
      <Tabs.Screen name="index" options={{ title: 'QUESTS', tabBarIcon: icon('⚔️') }} />
      <Tabs.Screen name="status" options={{ title: 'STATUS', tabBarIcon: icon('📊') }} />
      <Tabs.Screen name="vitals" options={{ title: 'VITALS', tabBarIcon: icon('❤️') }} />
      <Tabs.Screen name="shop" options={{ title: 'SHOP', tabBarIcon: icon('🛡️') }} />
      <Tabs.Screen name="guild" options={{ title: 'GUILD', tabBarIcon: icon('🏆') }} />
    </Tabs>
  );
}
