# NeverBeen — End-to-End Deployment Guide

How the three pieces of NeverBeen connect:

```
┌─────────────────────────────┐        HTTPS (CORS ok)        ┌──────────────────────────┐
│  Angular website            │  ───────────────────────────▶ │  NeverBeen API (.NET 7)  │
│  https://youneverbeen.      │   Authorization: Bearer JWT   │  Windows VPS / IIS       │
│  kingshukbanu1987.workers.dev│ ◀─────────────────────────── │  (Kestrel behind IIS)    │
└─────────────────────────────┘      JSON responses           └────────────┬─────────────┘
                                                                            │
                                                                            │ PostgreSQL (5432, SSL)
                                                                            ▼
                                                             ┌──────────────────────────┐
                                                             │  Supabase (PostgreSQL)   │
                                                             │  db.oiziwexxowcfjunigswz │
                                                             │  .supabase.co            │
                                                             └──────────────────────────┘
```

> **SECURITY FIRST — please do these two things before going live:**
> 1. **Rotate the Supabase database password.** It has been shared in chat and this
>    repository is **public**, and `appsettings.json` now contains it in plain text
>    (your choice). Rotate it in Supabase → *Database settings → Reset database
>    password*, then update `ConnectionStrings:Supabase` in `appsettings.json`.
> 2. **Guard the JWT signing key.** If someone gets `Jwt:SigningKey` they can forge
>    login tokens for any user. Never push this repository anywhere else without it.

---

## What was changed in this API so the worlds connect

| File | Change |
|---|---|
| `appsettings.json` | Real Supabase connection string (`ConnectionStrings:Supabase`), a strong `Jwt:SigningKey`, `Cors:AllowedOrigins` now includes `https://youneverbeen.kingshukbanu1987.workers.dev`, `OAuth:FrontendRedirectUri` points to your live site's `/auth/callback` |
| `appsettings.Development.json` | Removed the `localhost` connection override so the Supabase string is used in every environment |
| `Data/DbInitializer.cs` | **Supabase fix:** EF's `EnsureCreated` silently does nothing on Supabase (because built-in `auth.*`/`storage.*` tables already exist). The initializer now checks for the `Users` table and creates the schema from the EF model when missing |
| `Program.cs` | Swagger UI (`/swagger`) enabled in all environments so you can test the deployed API |

---

## Step 1 — Supabase (the database)

Nothing to install — Supabase is ready as-is.

1. Your connection string (already set in `appsettings.json`):
   `Host=db.oiziwexxowcfjunigswz.supabase.co;Database=postgres;Username=postgres;Password=...;SSL Mode=Require;Trust Server Certificate=true`
2. On first API start, all NeverBeen tables (`Users`, `Countries`, `Cities`, `JourneyPosts`, ...)
   are created automatically in the `public` schema and countries/cities are seeded.
   You can watch it happen in Supabase → *Table Editor*.
3. Optional but recommended: Supabase → *Database settings → Reset database password*
   (because of the warning above), then update `appsettings.json`.
4. Keep using the **Session pooler** connection string (`db.<ref>.supabase.co:5432`) —
   it is the right choice for EF Core / long-lived connections.

## Step 2 — The API on your Windows VPS

### 2.1 Install prerequisites

1. Install the **.NET 7 SDK** (simplest) or the **.NET 7 ASP.NET Core Runtime +
   Hosting Bundle** (if you will use IIS). Download from
   <https://dotnet.microsoft.com/en-us/download/dotnet/7.0>.
   > The **Hosting Bundle is required for IIS** — install it *after* IIS, or run
   > `Repair` on it afterwards so IIS picks up the ASP.NET Core module.
2. (IIS only) Make sure IIS is installed with the *ASP.NET Core Hosting Bundle*
   module visible in IIS → *Modules*.

### 2.2 Publish the API from your dev machine

```powershell
cd neverbeen-api
dotnet publish NeverBeen.API.csproj -c Release -o C:\publish\neverbeen-api
```

Copy `C:\publish\neverbeen-api` to the VPS (e.g. `C:\neverbeen-api`).

### 2.3 Run it (pick one)

**Option A — quick test / simple run (console):**

```powershell
cd C:\neverbeen-api
dotnet NeverBeen.API.dll --urls "http://0.0.0.0:5080"
```

Open `http://localhost:5080/health` on the VPS — it must return `Healthy`.
(To keep it running after logout, use IIS Option B, or register it as a service
with [NSSM](https://nssm.cc/) or Windows Task Scheduler "At startup".)

**Option B — IIS (recommended for a VPS):**

1. IIS → *Add Website*: name `neverbeen-api`, physical path `C:\neverbeen-api`,
   binding: pick a domain (see 2.4) or `http` on port `5080`.
2. Application Pool → *.NET CLR Version = **No Managed Code***.
3. Browse the site → `/health` must return `Healthy`.

### 2.4 IMPORTANT — the API must be served over **HTTPS**

Your Angular site is on `https://...workers.dev`. Browsers **block** a secure page
from calling an insecure (`http://`) API ("mixed content"), so the API needs a
public HTTPS address. Easiest options:

- **A domain + IIS certificate:** point e.g. `api.yourdomain.com` to the VPS IP,
  in IIS add an HTTPS binding and install a certificate (IIS *Server Certificates* →
  *Create Self-Signed Certificate* only works for testing; free production certs:
  Let's Encrypt / win-acme, or Cloudflare origin certificates).
- **Cloudflare in front of the VPS:** put `api.yourdomain.com` on Cloudflare
  (proxied, orange cloud) and let Cloudflare terminate SSL for you.
- **Tunnel without a public port:** `cloudflared tunnel` (free) publishes the local
  API under an `https://` hostname with no firewall changes at all.

Whichever you choose, your final API base URL looks like
`https://api.yourdomain.com` — remember it for Step 3.

### 2.5 Firewall

If the VPS is directly exposed (options 1–2), open the port you bound
(443 for HTTPS / 5080 for the quick test) in *Windows Defender Firewall →
Inbound Rules*, and in your cloud provider's security group if there is one.

## Step 3 — Point the Angular website at the API

1. In your **Angular repo** set the API base URL (typically
   `src/environments/environment.prod.ts`):

   ```ts
   export const environment = {
     production: true,
     apiBaseUrl: 'https://api.yourdomain.com'   // your API URL from Step 2.4
   };
   ```

   Make sure every HTTP service uses `environment.apiBaseUrl + '/api/...'` —
   and **not** `localhost` (the browser would try to reach *your visitor's* localhost).
2. Rebuild and redeploy the site to Cloudflare:

   ```powershell
   ng build --configuration production
   npx wrangler deploy        # or however you deploy the Workers site
   ```
3. CORS is already handled on the API side: `https://youneverbeen.kingshukbanu1987.workers.dev`
   is in `Cors:AllowedOrigins`. If you later add a custom domain for the site,
   add that origin to the same comma-separated list and restart the API.

## Step 4 — OAuth (Google / Facebook / Microsoft sign-in)

The OAuth "redirect URI" is your **frontend** callback URL — this exact value:

```
https://youneverbeen.kingshukbanu1987.workers.dev/auth/callback
```

Register it with each provider console:

| Provider | Where | Field |
|---|---|---|
| Google | Google Cloud Console → APIs & Services → Credentials → your OAuth client | *Authorized redirect URIs* |
| Facebook | developers.facebook.com → your app → Facebook Login → Settings | *Valid OAuth Redirect URIs* |
| Microsoft | portal.azure.com → App registrations → your app → Authentication | *Redirect URIs* |

Then copy each app's **Client ID / Client Secret** into `appsettings.json`
(`OAuth:Google`, `OAuth:Facebook`, `OAuth:Microsoft`) and restart the API.
If you skip OAuth, everything else still works.

## Step 5 — Verify everything (checklist)

1. `https://YOUR-API-HOST/health` → `Healthy` (API ↔ Supabase connection works).
2. `https://YOUR-API-HOST/swagger` → Swagger UI loads.
3. Supabase → *Table Editor* → `public` schema has `Users`, `Countries`, ... and
   countries are seeded (created on first API start).
4. Open the Angular site → DevTools → Network: an API call returns **200** and you
   see **no CORS error**.
5. Complete a sign-up / registration from the site, then a login; the row appears
   in Supabase → `Users`.
6. (If configured) OAuth sign-in round-trips back to `/auth/callback` and logs you in.

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| `/health` is Unhealthy | Connection string wrong, VPS cannot reach `db.oiziwexxowcfjunigswz.supabase.co:5432` (firewall/DNS), or Supabase project paused (free tier pauses after ~1 week idle) |
| "relation \"Users\" does not exist" | The schema was not created — check API startup logs; the fixed `DbInitializer` creates it automatically on start |
| CORS error in browser console | Frontend origin missing from `Cors:AllowedOrigins` in `appsettings.json` (restart the API after changing) |
| Browser blocks requests ("mixed content") | The API is on `http://` while the site is `https://` — finish Step 2.4 |
| OAuth error `redirect_uri_mismatch` | Provider console has a different/older redirect URI — use exactly `https://youneverbeen.kingshukbanu1987.workers.dev/auth/callback` |
| 401 Unauthorized from API calls | Angular is not sending `Authorization: Bearer <token>`, or `Jwt:SigningKey` differs between the running API and `appsettings.json` |
