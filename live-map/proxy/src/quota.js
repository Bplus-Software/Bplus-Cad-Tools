// Pure logic: limits, periods (US Pacific, same as Google's quota reset and billing), install tokens.
export const DEFAULT_LIMITS = { globalMonth: 99000, globalDay: 3200, userMonth: 5000, registerPerIpDay: 5 };

export function limitsFromEnv(env) {
  const n = (v, d) => { const x = parseInt(v, 10); return Number.isFinite(x) && x >= 0 ? x : d; };
  return {
    globalMonth: n(env.GLOBAL_MONTH_CAP, DEFAULT_LIMITS.globalMonth),
    globalDay: n(env.GLOBAL_DAY_CAP, DEFAULT_LIMITS.globalDay),
    userMonth: n(env.USER_MONTH_CAP, DEFAULT_LIMITS.userMonth),
    registerPerIpDay: n(env.REGISTER_PER_IP_DAY, DEFAULT_LIMITS.registerPerIpDay),
  };
}

export function pacificPeriods(date = new Date()) {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Los_Angeles', year: 'numeric', month: '2-digit', day: '2-digit' }).format(date);
  return { day: parts, month: parts.slice(0, 7) };
}

// counts: {gMonth, gDay, uMonth}. Returns null if allowed, else a reason code.
export function decide(counts, limits) {
  if (counts.gDay >= limits.globalDay) return 'daily_cap';
  if (counts.gMonth >= limits.globalMonth) return 'monthly_cap';
  if (counts.uMonth >= limits.userMonth) return 'user_quota';
  return null;
}

const enc = new TextEncoder();
async function hmac(secret, msg) {
  const key = await crypto.subtle.importKey('raw', enc.encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  const sig = new Uint8Array(await crypto.subtle.sign('HMAC', key, enc.encode(msg)));
  return Array.from(sig.slice(0, 16)).map(b => b.toString(16).padStart(2, '0')).join('');
}
export function newInstallId() {
  const b = crypto.getRandomValues(new Uint8Array(12));
  return Array.from(b).map(x => x.toString(16).padStart(2, '0')).join('');
}
export async function makeToken(secret, id) { return id + '.' + await hmac(secret, id); }
export async function verifyToken(secret, token) {
  if (typeof token !== 'string') return null;
  const m = /^([0-9a-f]{24})\.([0-9a-f]{32})$/.exec(token);
  if (!m) return null;
  const want = await hmac(secret, m[1]);
  let diff = 0; for (let i = 0; i < 32; i++) diff |= want.charCodeAt(i) ^ m[2].charCodeAt(i);
  return diff === 0 ? m[1] : null;
}
export const MESSAGES = {
  daily_cap: 'Today\'s shared map limit has been reached. It resets at 10:00 Riyadh time (midnight Pacific).',
  monthly_cap: 'The shared monthly map limit has been reached. Maps return next month, or use your own Google key in BPGMAPSET.',
  user_quota: 'Your monthly map quota on this server is used up. Maps return next month, or use your own Google key in BPGMAPSET.',
  revoked: 'This installation has been blocked. Contact BPlus Software.',
  bad_token: 'This installation is not registered. Restart AutoCAD to register again.',
  register_limit: 'Too many registrations from this network today. Try again tomorrow.',
};
