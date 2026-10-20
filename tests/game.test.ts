import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  addDays,
  buyItem,
  CLEAR_BONUS,
  completePenalty,
  deleteQuest,
  diffDays,
  getRank,
  logRaid,
  newGameState,
  penaltyLoss,
  rollover,
  setVitals,
  toggleQuest,
  upsertQuest,
  xpForLevel,
} from '../lib/game.ts';
import type { GameState } from '../lib/types.ts';

const D = '2026-03-10';
const fresh = (): GameState => ({ ...newGameState('Tester'), day: D });
const clearAll = (s: GameState) => s.quests.reduce((acc, q) => toggleQuest(acc, q.id).state, s);

test('dates', () => {
  assert.equal(addDays('2026-02-28', 1), '2026-03-01');
  assert.equal(addDays('2026-12-31', 1), '2027-01-01');
  assert.equal(diffDays('2026-03-10', '2026-03-13'), 3);
});

test('completing a quest grants XP, skill XP and gold; undo reverses it', () => {
  const s = fresh();
  const q = s.quests[0];
  const done = toggleQuest(s, q.id).state;
  assert.equal(done.xp, q.xp);
  assert.equal(done.skills[q.skill].xp, q.xp);
  assert.ok(done.gold > 0);
  const undone = toggleQuest(done, q.id).state;
  assert.equal(undone.xp, 0);
  assert.equal(undone.gold, 0);
  assert.deepEqual(undone.completed, []);
});

test('clearing every quest adds the bonus and a streak; un-ticking one revokes it', () => {
  const s = clearAll(fresh());
  const questXp = s.quests.reduce((a, q) => a + q.xp, 0);
  assert.equal(s.totalXp, questXp + CLEAR_BONUS.xp);
  assert.equal(s.streak, 1);
  assert.equal(s.history[D].cleared, true);
  const back = toggleQuest(s, s.quests[0].id).state;
  assert.equal(back.streak, 0);
  assert.equal(back.history[D].cleared, false);
});

test('level up carries leftover XP and reports rank changes', () => {
  const s = { ...fresh(), lv: 2, xp: xpForLevel(2) - 10 };
  const { state, events } = toggleQuest(s, 'pushups'); // 50 XP
  assert.equal(state.lv, 3);
  assert.equal(state.xp, 40);
  assert.deepEqual(events.find((e) => e.type === 'levelUp'), { type: 'levelUp', lv: 3, rankUp: true });
  assert.equal(getRank(3).r, 'D');
});

test('rollover after a cleared day keeps the streak and assigns no penalty', () => {
  const s = clearAll(fresh());
  const { state, events } = rollover(s, addDays(D, 1));
  assert.equal(state.streak, 1);
  assert.equal(state.penalty, null);
  assert.deepEqual(state.completed, []);
  assert.equal(events.length, 0);
});

test('rollover after a missed day breaks the streak and assigns a penalty quest', () => {
  const s = { ...fresh(), streak: 5 };
  const { state, events } = rollover(s, addDays(D, 1));
  assert.equal(state.streak, 0);
  assert.deepEqual(state.penalty, { active: true, done: false });
  assert.ok(events.some((e) => e.type === 'streakLost'));
  assert.ok(events.some((e) => e.type === 'penaltyAssigned'));
});

test('an unfinished penalty costs XP but never a level', () => {
  const s: GameState = { ...fresh(), lv: 4, xp: 30, totalXp: 2000, penalty: { active: true, done: false } };
  const { state, events } = rollover(s, addDays(D, 1));
  assert.equal(state.lv, 4);
  assert.equal(state.xp, 0);
  assert.deepEqual(events.find((e) => e.type === 'penaltyFailed'), { type: 'penaltyFailed', xpLost: 30 });
  assert.ok(penaltyLoss(4) > 30);
});

test('completing the penalty avoids the XP loss', () => {
  let s: GameState = { ...fresh(), xp: 100, totalXp: 100, penalty: { active: true, done: false } };
  s = completePenalty(s).state;
  s = clearAll(s);
  const { state, events } = rollover(s, addDays(D, 1));
  assert.ok(!events.some((e) => e.type === 'penaltyFailed'));
  assert.equal(state.penalty, null);
});

test('shields absorb missed days, including days the app was closed', () => {
  const s: GameState = { ...fresh(), streak: 9, inventory: { ...fresh().inventory, shields: 2 } };
  const { state, events } = rollover(s, addDays(D, 3)); // D, D+1, D+2 all missed
  assert.equal(events.filter((e) => e.type === 'shieldUsed').length, 2);
  assert.equal(state.inventory.shields, 0);
  assert.equal(state.streak, 0); // third miss had no shield
  assert.ok(state.penalty?.active);
  assert.equal(state.history[addDays(D, 2)].cleared, false);
});

test('auto quests complete when vitals reach their goal', () => {
  const s = fresh();
  const { state } = setVitals(s, { steps: 12000 });
  assert.ok(state.completed.includes('steps'));
  assert.ok(!state.completed.includes('water'));
});

test('weekly raid: 7 consecutive days pays out, a gap restarts', () => {
  let s = fresh();
  for (let i = 0; i < 6; i++) s = logRaid(s, addDays(D, i)).state;
  assert.equal(s.raid.days, 6);
  const gap = logRaid(s, addDays(D, 8)).state;
  assert.equal(gap.raid.days, 1);
  const { state, events } = logRaid(s, addDays(D, 6));
  assert.equal(state.raid.clears, 1);
  assert.equal(state.raid.days, 0);
  assert.ok(events.some((e) => e.type === 'raidCleared'));
});

test('shop: buying requires gold, equips themes, caps shields', () => {
  const poor = buyItem(fresh(), 'theme:crimson');
  assert.equal(poor.ok, false);
  const rich = { ...fresh(), gold: 1000 };
  const bought = buyItem(rich, 'theme:crimson');
  assert.ok(bought.ok && bought.state.inventory.theme === 'crimson' && bought.state.gold === 850);
  let s = rich;
  for (let i = 0; i < 3; i++) {
    const r = buyItem(s, 'shield');
    if (!r.ok) assert.fail(r.reason);
    s = r.state;
  }
  assert.equal(buyItem(s, 'shield').ok, false);
});

test('adding a quest after clearing un-clears the day; deleting the last open one clears it', () => {
  const s = clearAll(fresh());
  const added = upsertQuest(s, { id: 'x', name: 'Extra', desc: '', icon: '⚔️', xp: 10, skill: 'focus', auto: null }).state;
  assert.equal(added.streak, 0);
  const removed = deleteQuest(added, 'x');
  assert.equal(removed.state.streak, 1);
  assert.ok(removed.events.some((e) => e.type === 'cleared'));
});
