import { limitsFromEnv, pacificPeriods, decide, newInstallId, makeToken, verifyToken, MESSAGES } from './quota.js';

const GOOGLE = 'https://tile.googleapis.com';
const json = (obj, status = 200, extra = {}) => new Response(JSON.stringify(obj), { status, headers: { 'content-type': 'application/json', 'cache-control': 'no-store', ...extra } });
const fail = (code, status) => json({ error: { code, message: MESSAGES[code] || code } }, status);

export default {
  async fetch(req, env) {
    const url = new URL(req.url);
    const meter = env.METER.get(env.METER.idFromName('main'));
    try {
      if (url.pathname === '/healthz') return json({ ok: true });
      if (url.pathname === '/v1/register' && req.method === 'POST') {
        const ip = req.headers.get('cf-connecting-ip') || 'unknown';
        const id = newInstallId();
        const r = await meter.fetch('https://m/register', { method: 'POST', body: JSON.stringify({ id, ip, limits: limitsFromEnv(env), p: pacificPeriods() }) }).then(x => x.json());
        if (r.error) return fail(r.error, 429);
        return json({ token: await makeToken(env.TOKEN_SECRET, id) });
      }
      // From here the plugin sends its install token where Google would take "key".
      const id = await verifyToken(env.TOKEN_SECRET, url.searchParams.get('key'));
      if (!id) return fail('bad_token', 401);
      url.searchParams.set('key', env.GOOGLE_KEY);

      if (url.pathname === '/v1/usage') {
        const u = await meter.fetch('https://m/usage', { method: 'POST', body: JSON.stringify({ id, p: pacificPeriods(), limits: limitsFromEnv(env) }) }).then(x => x.json());
        return json(u);
      }
      if (url.pathname === '/v1/createSession' && req.method === 'POST') {
        const chk = await meter.fetch('https://m/check', { method: 'POST', body: JSON.stringify({ id }) }).then(x => x.json());
        if (chk.revoked) return fail('revoked', 403);
        return passthrough(await fetch(GOOGLE + '/v1/createSession?key=' + encodeURIComponent(env.GOOGLE_KEY), { method: 'POST', headers: { 'content-type': 'application/json' }, body: await req.text() }));
      }
      if (url.pathname === '/tile/v1/viewport' && req.method === 'GET') {
        return passthrough(await fetch(GOOGLE + url.pathname + url.search));
      }
      if (/^\/v1\/2dtiles\/\d+\/\d+\/\d+$/.test(url.pathname) && req.method === 'GET') {
        const r = await meter.fetch('https://m/tile', { method: 'POST', body: JSON.stringify({ id, limits: limitsFromEnv(env), p: pacificPeriods() }) }).then(x => x.json());
        if (r.error) return fail(r.error, r.error === 'revoked' ? 403 : 429);
        return passthrough(await fetch(GOOGLE + url.pathname + url.search));
      }
      return json({ error: { code: 'not_found' } }, 404);
    } catch (e) {
      return json({ error: { code: 'proxy_error', message: 'Map server error.' } }, 502);
    }
  },
};

// Pass Google's answer through unchanged, including Cache-Control and ETag, so the plugin applies Google's own caching rules. Nothing is stored here.
function passthrough(res) {
  const h = new Headers();
  for (const k of ['content-type', 'cache-control', 'etag', 'expires', 'last-modified']) { const v = res.headers.get(k); if (v) h.set(k, v); }
  return new Response(res.body, { status: res.status, headers: h });
}

// One Durable Object does all counting, so the caps are exact (no races between requests).
export class Meter {
  constructor(state) {
    this.sql = state.storage.sql;
    this.sql.exec('CREATE TABLE IF NOT EXISTS c (scope TEXT, period TEXT, n INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(scope, period))');
    this.sql.exec('CREATE TABLE IF NOT EXISTS revoked (id TEXT PRIMARY KEY)');
  }
  get(scope, period) { const r = this.sql.exec('SELECT n FROM c WHERE scope=? AND period=?', scope, period).toArray(); return r.length ? r[0].n : 0; }
  add(scope, period) { this.sql.exec('INSERT INTO c(scope,period,n) VALUES(?,?,1) ON CONFLICT(scope,period) DO UPDATE SET n=n+1', scope, period); }
  isRevoked(id) { return this.sql.exec('SELECT 1 FROM revoked WHERE id=?', id).toArray().length > 0; }
  async fetch(req) {
    const path = new URL(req.url).pathname; const b = await req.json();
    if (path === '/check') return Response.json({ revoked: this.isRevoked(b.id) });
    if (path === '/register') {
      if (this.get('ip:' + b.ip, b.p.day) >= b.limits.registerPerIpDay) return Response.json({ error: 'register_limit' });
      this.add('ip:' + b.ip, b.p.day); return Response.json({ ok: true });
    }
    const counts = () => ({ gMonth: this.get('g', b.p.month), gDay: this.get('g', b.p.day), uMonth: this.get('u:' + b.id, b.p.month) });
    if (path === '/usage') { const c = counts(); return Response.json({ globalMonthUsed: c.gMonth, globalMonthCap: b.limits.globalMonth, globalDayUsed: c.gDay, globalDayCap: b.limits.globalDay, userMonthUsed: c.uMonth, userMonthCap: b.limits.userMonth }); }
    if (path === '/tile') {
      if (this.isRevoked(b.id)) return Response.json({ error: 'revoked' });
      const why = decide(counts(), b.limits);
      if (why) return Response.json({ error: why });
      this.add('g', b.p.month); this.add('g', b.p.day); this.add('u:' + b.id, b.p.month);
      return Response.json({ ok: true });
    }
    return Response.json({ error: 'not_found' }, { status: 404 });
  }
}
