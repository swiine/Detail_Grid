import * as Clipboard from 'expo-clipboard';
import { useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { ActivityIndicator, Alert, Platform, Pressable, Share, StyleSheet, Text, View } from 'react-native';

import { HunterHeader } from '@/components/HunterHeader';
import { Avatar, Button, Card, Field, Screen, Section, T } from '@/components/ui';
import { backend } from '@/lib/backend';
import { getRank } from '@/lib/game';
import { C, F } from '@/lib/theme';
import type { FriendRequest, PublicProfile } from '@/lib/types';
import { useGame } from '@/state/store';

export default function GuildScreen() {
  const { game, user, pal, showToast, sync } = useGame();
  const [me, setMe] = useState<PublicProfile | null>(null);
  const [board, setBoard] = useState<PublicProfile[]>([]);
  const [requests, setRequests] = useState<FriendRequest[]>([]);
  const [code, setCode] = useState('');
  const [loading, setLoading] = useState(true);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setError('');
    try {
      const [p, b, r] = await Promise.all([backend.myProfile(), backend.leaderboard(), backend.friendRequests()]);
      setMe(p);
      setBoard(b);
      setRequests(r);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  // Reload on focus, and after a save so my own row reflects the latest XP.
  useFocusEffect(
    useCallback(() => {
      if (sync === 'idle') load();
    }, [load, sync]),
  );

  const send = async () => {
    setSending(true);
    try {
      const res = await backend.sendFriendRequest(code);
      showToast(res === 'sent' ? 'Request sent!' : res === 'accepted' ? 'Friend added!' : 'Already in your guild');
      setCode('');
      load();
    } catch (e) {
      showToast((e as Error).message);
    } finally {
      setSending(false);
    }
  };

  const respond = async (id: string, accept: boolean) => {
    await backend.respondFriendRequest(id, accept).catch((e) => showToast(e.message));
    load();
  };

  const remove = (p: PublicProfile) => {
    const go = () => backend.removeFriend(p.id).then(load).catch((e) => showToast(e.message));
    if (Platform.OS === 'web') {
      if (window.confirm(`Remove ${p.hunterName} from your guild?`)) go();
    } else {
      Alert.alert('Remove friend', `Remove ${p.hunterName} from your guild?`, [
        { text: 'Cancel', style: 'cancel' },
        { text: 'Remove', style: 'destructive', onPress: go },
      ]);
    }
  };

  const shareCode = async () => {
    if (!me) return;
    if (Platform.OS === 'web') {
      await Clipboard.setStringAsync(me.friendCode);
      return showToast('Code copied');
    }
    Share.share({ message: `Join my guild in The Solo's Ascent! My hunter code: ${me.friendCode}` });
  };

  return (
    <Screen header={<HunterHeader />}>
      <Card glow>
        <Text style={[styles.title, { color: pal.p3 }]}>🏆 Hunter Guild</Text>
        <T style={{ marginTop: 3 }}>Add friends by hunter code and race up the ranks together.</T>
        <View style={styles.codeRow}>
          <View>
            <T kind="tiny">YOUR HUNTER CODE</T>
            <Text style={[styles.code, { color: pal.p2 }]} selectable>
              {me?.friendCode ?? '—'}
            </Text>
          </View>
          <Button title={Platform.OS === 'web' ? 'Copy' : 'Share'} variant="ghost" onPress={shareCode} style={{ marginTop: 0 }} />
        </View>
        {backend.mode === 'demo' && (
          <T kind="muted" style={{ marginTop: 6 }}>
            Demo mode: rivals are simulated. Any code like HNTR-AB12 adds a test hunter.
          </T>
        )}
      </Card>

      <Section title="Add friend" />
      <Card>
        <Field
          value={code}
          onChangeText={(t) => setCode(t.toUpperCase())}
          placeholder="HNTR-XXXX"
          autoCapitalize="characters"
          autoCorrect={false}
          maxLength={9}
          onSubmitEditing={send}
        />
        <Button title="Send request" onPress={send} loading={sending} disabled={code.trim().length < 9} style={{ marginTop: 0 }} />
      </Card>

      {requests.length > 0 && (
        <>
          <Section title={`Requests (${requests.length})`} />
          {requests.map((r) => (
            <Card key={r.id}>
              <View style={styles.row}>
                <Avatar name={r.from.hunterName} frame={r.from.frame} size={34} />
                <View style={{ flex: 1 }}>
                  <T kind="label">{r.from.hunterName}</T>
                  <T kind="tiny">
                    Lv {r.from.lv} · Rank {getRank(r.from.lv).r}
                  </T>
                </View>
                <Pressable onPress={() => respond(r.id, false)} hitSlop={6}>
                  <Text style={[styles.action, { color: C.text3 }]}>DECLINE</Text>
                </Pressable>
                <Pressable onPress={() => respond(r.id, true)} hitSlop={6}>
                  <Text style={[styles.action, { color: pal.p }]}>ACCEPT</Text>
                </Pressable>
              </View>
            </Card>
          ))}
        </>
      )}

      <Section title="Leaderboard" />
      {loading ? (
        <ActivityIndicator color={pal.p} style={{ marginTop: 20 }} />
      ) : error ? (
        <Card>
          <T style={{ color: C.danger }}>{error}</T>
          <Button title="Retry" variant="ghost" onPress={load} />
        </Card>
      ) : (
        board.map((p, i) => {
          const isMe = p.id === me?.id || p.id === user?.id;
          const rk = getRank(p.lv);
          return (
            <Pressable key={p.id} onLongPress={() => !isMe && remove(p)}>
              <View style={[styles.lb, { borderColor: isMe ? pal.p : pal.border }, isMe && { backgroundColor: pal.pl }]}>
                <Text style={[styles.place, i < 3 && { color: [C.gold, '#C0C0D0', '#CD7F32'][i] }]}>#{i + 1}</Text>
                <Avatar name={p.hunterName} frame={p.frame} size={34} color={rk.color} />
                <View style={{ flex: 1 }}>
                  <T kind="label" numberOfLines={1}>
                    {isMe ? `${game.hunterName} (you)` : p.hunterName}
                  </T>
                  <T kind="tiny">
                    Lv {p.lv} · 🔥 {p.streak}
                  </T>
                </View>
                <View style={{ alignItems: 'flex-end' }}>
                  <Text style={[styles.rank, { color: rk.color }]}>{rk.r}</Text>
                  <T kind="tiny">{p.totalXp.toLocaleString()} XP</T>
                </View>
              </View>
            </Pressable>
          );
        })
      )}
      {!loading && board.length <= 1 && !error && (
        <T kind="muted" style={{ textAlign: 'center', marginTop: 6 }}>
          Your guild is empty. Share your code to recruit hunters.
        </T>
      )}
      {board.length > 1 && (
        <T kind="muted" style={{ textAlign: 'center', marginTop: 4 }}>
          Long-press a hunter to remove them.
        </T>
      )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  title: { fontFamily: F.title, fontSize: 16, letterSpacing: 1 },
  codeRow: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginTop: 12 },
  code: { fontFamily: F.label, fontSize: 24, letterSpacing: 3 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 10 },
  action: { fontFamily: F.label, fontSize: 12, letterSpacing: 1, marginLeft: 6 },
  lb: { flexDirection: 'row', alignItems: 'center', gap: 10, backgroundColor: C.card, borderWidth: 1, borderRadius: 11, padding: 10, marginBottom: 7 },
  place: { fontFamily: F.label, fontSize: 15, color: C.text3, width: 30 },
  rank: { fontFamily: F.titleBlack, fontSize: 16 },
});
