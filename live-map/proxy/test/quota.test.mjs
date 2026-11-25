import test from 'node:test';
import assert from 'node:assert/strict';
import { decide, DEFAULT_LIMITS, pacificPeriods, makeToken, verifyToken, newInstallId, limitsFromEnv } from '../src/quota.js';
import { Meter } from '../src/index.js';

const L = DEFAULT_LIMITS;
test('decide order and thresholds', () => {
  assert.equal(decide({ gMonth: 0, gDay: 0, uMonth: 0 }, L), null);
  assert.equal(decide({ gMonth: 98999, gDay: 0, uMonth: 0 }, L), null);
  assert.equal(decide({ gMonth: 99000, gDay: 0, uMonth: 0 }, L), 'monthly_cap');
  assert.equal(decide({ gMonth: 0, gDay: 3200, uMonth: 0 }, L), 'daily_cap');
  assert.equal(decide({ gMonth: 0, gDay: 0, uMonth: 5000 }, L), 'user_quota');
});
test('pacific period crosses at Pacific midnight', () => {
  assert.deepEqual(pacificPeriods(new Date('2026-12-01T07:59:00Z')), { day: '2026-11-30', month: '2026-11' });
  assert.deepEqual(pacificPeriods(new Date('2026-12-01T08:01:00Z')), { day: '2026-12-01', month: '2026-12' });
});
test('tokens', async () => {
  const id = newInstallId(); const t = await makeToken('s3cret', id);
  assert.equal(await verifyToken('s3cret', t), id);
  assert.equal(await verifyToken('other', t), null);
  assert.equal(await verifyToken('s3cret', t.slice(0, -1) + (t.endsWith('0') ? '1' : '0')), null);
  assert.equal(await verifyToken('s3cret', 'junk'), null);
  assert.equal(await verifyToken('s3cret', undefined), null);
});
test('env limits fall back to defaults', () => {
  assert.deepEqual(limitsFromEnv({}), L);
  assert.equal(limitsFromEnv({ GLOBAL_MONTH_CAP: '100' }).globalMonth, 100);
  assert.equal(limitsFromEnv({ GLOBAL_MONTH_CAP: 'x' }).globalMonth, L.globalMonth);
});

// Minimal fake of the Durable Object SQL API, enough to run the real Meter code.
function fakeState() {
  const c = new Map(); const rev = new Set();
  return { storage: { sql: { exec(q, ...a) {
    if (q.startsWith('CREATE')) return { toArray: () => [] };
    if (q.startsWith('SELECT n')) { const v = c.get(a[0] + '|' + a[1]); return { toArray: () => v === undefined ? [] : [{ n: v }] }; }
    if (q.startsWith('INSERT INTO c')) { const k = a[0] + '|' + a[1]; c.set(k, (c.get(k) || 0) + 1); return { toArray: () => [] }; }
    if (q.startsWith('SELECT 1')) return { toArray: () => rev.has(a[0]) ? [{}] : [] };
  } } }, rev };
}
const call = (m, path, body) => m.fetch(new Request('https://m' + path, { method: 'POST', body: JSON.stringify(body) })).then(r => r.json());
test('meter enforces global cap exactly, per-user quota, and register limit', async () => {
  const st = fakeState(); const m = new Meter(st);
  const lim = { globalMonth: 10, globalDay: 8, userMonth: 5, registerPerIpDay: 2 };
  const p = { day: '2026-11-25', month: '2026-11' };
  let ok = 0; for (let i = 0; i < 9; i++) { const r = await call(m, '/tile', { id: 'a', limits: lim, p }); if (r.ok) ok++; }
  assert.equal(ok, 5); // user quota of 'a'
  assert.equal((await call(m, '/tile', { id: 'a', limits: lim, p })).error, 'user_quota');
  for (let i = 0; i < 3; i++) assert.ok((await call(m, '/tile', { id: 'b', limits: lim, p })).ok);
  assert.equal((await call(m, '/tile', { id: 'c', limits: lim, p })).error, 'daily_cap'); // 8 used today
  const next = { day: '2026-11-26', month: '2026-11' };
  assert.ok((await call(m, '/tile', { id: 'c', limits: lim, p: next })).ok);
  assert.ok((await call(m, '/tile', { id: 'c', limits: lim, p: next })).ok);
  assert.equal((await call(m, '/tile', { id: 'd', limits: lim, p: next })).error, 'monthly_cap'); // 10 this month
  assert.ok((await call(m, '/tile', { id: 'd', limits: lim, p: { day: '2026-12-01', month: '2026-12' } })).ok);
  st.rev.add('z'); assert.equal((await call(m, '/tile', { id: 'z', limits: lim, p: next })).error, 'revoked');
  assert.ok((await call(m, '/register', { ip: '1.1.1.1', limits: lim, p })).ok);
  assert.ok((await call(m, '/register', { ip: '1.1.1.1', limits: lim, p })).ok);
  assert.equal((await call(m, '/register', { ip: '1.1.1.1', limits: lim, p })).error, 'register_limit');
});
