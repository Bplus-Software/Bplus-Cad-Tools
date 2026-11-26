# BPlus map server (Cloudflare Worker)

Sits between the plugin and Google Map Tiles. The Google key lives only here, as a secret. The plugin sends a per-install token instead of a key.

Limits (all in `wrangler.toml`, change and redeploy): `GLOBAL_MONTH_CAP` (default 99000, all users, Pacific month), `GLOBAL_DAY_CAP` (default 3200, Pacific day; keep it at or below the Google daily quota), `USER_MONTH_CAP` (5000 per install), `REGISTER_PER_IP_DAY` (5). Counting is done in one Durable Object, so the caps are exact. Every tile request is counted before it is sent to Google. When a cap is hit the plugin shows a plain message and the server stops calling Google.

The server stores no tiles. It passes Google's Cache-Control and ETag headers through, and the plugin keeps tiles in memory only, within those headers.

Setup (secrets are never committed): `npm i`, `npx wrangler secret put GOOGLE_KEY`, `npx wrangler secret put TOKEN_SECRET` (any long random string), `npx wrangler deploy`. Test: `npm test`.
Block an install: add its id to the `revoked` table (first 24 hex characters of its token).
Also set the Google Cloud quota "Map Tiles API 2D tile requests per day" to 3,225 for a hard no-bill guarantee (100,000 / 31).
