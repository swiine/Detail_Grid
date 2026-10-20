import { useState } from 'react';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { HunterHeader } from '@/components/HunterHeader';
import { Avatar, Card, Chip, Screen, Section, T } from '@/components/ui';
import { buyItem, equipItem, goldFor, MAX_SHIELDS, SHOP, type ShopItem, type ShopKind } from '@/lib/game';
import { C, F, palette } from '@/lib/theme';
import { useGame } from '@/state/store';

const FILTERS: { key: ShopKind | null; label: string }[] = [
  { key: null, label: 'ALL' },
  { key: 'theme', label: 'THEMES' },
  { key: 'frame', label: 'FRAMES' },
  { key: 'shield', label: 'SHIELDS' },
];

export default function ShopScreen() {
  const { game, pal, update, showToast } = useGame();
  const [filter, setFilter] = useState<ShopKind | null>(null);
  const items = filter ? SHOP.filter((i) => i.kind === filter) : SHOP;

  const press = (item: ShopItem) => {
    const owned = game.inventory.owned.includes(item.id);
    if (owned) {
      update((s) => equipItem(s, item.id));
      return showToast(`Equipped ${item.name}`);
    }
    const res = buyItem(game, item.id);
    if (!res.ok) return showToast(res.reason);
    update(() => res.state);
    showToast(`Purchased ${item.name}!`);
  };

  return (
    <Screen header={<HunterHeader />}>
      <Card glow>
        <Text style={[styles.title, { color: pal.p3 }]}>◈ Hunter&apos;s Shop</Text>
        <T style={{ marginTop: 3 }}>Spend gold earned from quests on cosmetics and streak protection.</T>
        <View style={styles.balance}>
          <Text style={styles.gold}>🪙 {game.gold.toLocaleString()} gold</Text>
          <T kind="tiny">
            🛡️ {game.inventory.shields}/{MAX_SHIELDS} shields
          </T>
        </View>
        <T kind="muted" style={{ marginTop: 6 }}>
          Earn gold: each quest gives XP ÷ 5 (e.g. a 50 XP quest = {goldFor(50)} 🪙), +25 for clearing the day, +60 per weekly raid.
        </T>
      </Card>

      <View style={styles.filters}>
        {FILTERS.map((f) => (
          <Chip key={f.label} label={f.label} active={filter === f.key} onPress={() => setFilter(f.key)} />
        ))}
      </View>

      {(['theme', 'frame', 'shield'] as const)
        .filter((k) => !filter || filter === k)
        .map((kind) => (
          <View key={kind}>
            <Section title={kind === 'theme' ? 'Color themes' : kind === 'frame' ? 'Avatar frames' : 'Consumables'} />
            {items
              .filter((i) => i.kind === kind)
              .map((item) => {
                const owned = game.inventory.owned.includes(item.id);
                const key = item.id.split(':')[1];
                const equipped = (kind === 'theme' && game.inventory.theme === key) || (kind === 'frame' && game.inventory.frame === key);
                const afford = game.gold >= item.cost;
                return (
                  <Pressable
                    key={item.id}
                    onPress={() => !equipped && press(item)}
                    style={({ pressed }) => [styles.item, { borderColor: equipped ? pal.p : pal.border }, pressed && { opacity: 0.75 }]}>
                    <View style={[styles.preview, { borderColor: pal.border }]}>
                      {kind === 'theme' ? (
                        <View style={[styles.swatch, { backgroundColor: palette(key).p, shadowColor: palette(key).p }]} />
                      ) : kind === 'frame' ? (
                        <Avatar name={game.hunterName} frame={key} size={30} />
                      ) : (
                        <Text style={{ fontSize: 22 }}>{item.icon}</Text>
                      )}
                    </View>
                    <View style={{ flex: 1 }}>
                      <T kind="label">{item.name}</T>
                      <T kind="muted">{item.desc}</T>
                    </View>
                    <View style={[styles.tag, equipped ? { backgroundColor: pal.pm } : owned ? { borderColor: pal.border2, borderWidth: 1 } : null]}>
                      <Text
                        style={[
                          styles.tagText,
                          { color: equipped ? '#fff' : owned ? pal.p2 : afford ? C.gold : C.text3 },
                        ]}>
                        {equipped ? 'EQUIPPED' : owned ? 'EQUIP' : item.cost === 0 ? 'FREE' : `🪙 ${item.cost}`}
                      </Text>
                    </View>
                  </Pressable>
                );
              })}
          </View>
        ))}
    </Screen>
  );
}

const styles = StyleSheet.create({
  title: { fontFamily: F.title, fontSize: 16, letterSpacing: 1 },
  balance: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginTop: 10 },
  gold: { fontFamily: F.label, fontSize: 20, color: C.gold },
  filters: { flexDirection: 'row', gap: 7, marginTop: 10, flexWrap: 'wrap' },
  item: { backgroundColor: C.card, borderWidth: 1, borderRadius: 11, padding: 12, marginBottom: 7, flexDirection: 'row', alignItems: 'center', gap: 11 },
  preview: { width: 46, height: 46, borderRadius: 8, borderWidth: 1, backgroundColor: C.bg2, alignItems: 'center', justifyContent: 'center' },
  swatch: { width: 24, height: 24, borderRadius: 12, shadowOpacity: 0.9, shadowRadius: 8 },
  tag: { paddingHorizontal: 8, paddingVertical: 4, borderRadius: 4 },
  tagText: { fontFamily: F.label, fontSize: 12, letterSpacing: 0.5 },
});
