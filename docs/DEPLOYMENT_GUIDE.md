# NijMitra — Deployment Guide (Windows Server + IIS + PostgreSQL)

Target: **https://nijmitra.com**. Recommended topology: one IIS site; the ASP.NET Core API serves the built PWA from `wwwroot` (same origin → simplest cookies, CORS and CSP). Split hosting (static site + `api.` subdomain) is supported — see the end.

## 1. Server prerequisites
* Windows Server 2019/2022, IIS with **WebSocket Protocol** and **URL Rewrite** module.
* **ASP.NET Core Hosting Bundle for .NET 10** (restart IIS after install: `net stop was /y; net start w3svc`).
* PostgreSQL 14+ (local or managed). Create a database and a dedicated login:
  ```sql
  CREATE ROLE nijmitra_app LOGIN PASSWORD '<strong password>';
  CREATE DATABASE nijmitraapp OWNER nijmitra_app;
  ```
  The `pg_trgm` extension is created by the first migration; if the app login may not create extensions, run `CREATE EXTENSION pg_trgm;` once as a superuser in the database.
* A build machine with .NET SDK 10 and Node.js 20+ (can be your PC).

## 2. Build & publish
```powershell
.\deployment\scripts\publish.ps1 -Output C:\deploy\nijmitra
```
This runs `npm ci && npm run build` (PWA), the tests (if `NIJMITRA_TEST_DB` is set), `dotnet publish -c Release`, copies `NIJMITRA.Web/dist` into `wwwroot`, and adds `deployment/iis/web.config`.
Manual equivalent:
```powershell
cd NIJMITRA.Web; npm ci; npm run build; cd ..
dotnet publish NIJMITRA.API -c Release -o C:\deploy\nijmitra
Copy-Item -Recurse NIJMITRA.Web\dist\* C:\deploy\nijmitra\wwwroot\
Copy-Item deployment\iis\web.config C:\deploy\nijmitra\
```

## 3. Environment configuration
Settings come from `appsettings.json` → `appsettings.{Environment}.json` → optional `appsettings.Local.json` → environment variables prefixed `NIJMITRA_` (`:` written as `__`). **No secrets are committed to Git.**

| Variable | Required | Example / note |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | yes | `Production` (set in web.config) |
| `NIJMITRA_ConnectionStrings__DefaultConnection` | yes* | `Host=…;Port=5432;Database=nijmitraapp;Username=nijmitra_app;Password=…` (*can later be changed in Admin → Database) |
| `NIJMITRA_Jwt__SigningKey` | yes | 64 random bytes, base64 — `deployment/scripts/generate-secrets.ps1` |
| `NIJMITRA_Security__DataEncryptionKey` | yes | 32 random bytes, base64 — **back it up**; losing it makes encrypted diary entries, 2FA secrets and provider keys unreadable |
| `NIJMITRA_App__PublicBaseUrl` | yes | `https://nijmitra.com` |
| `NIJMITRA_App__BootstrapSuperAdminMobiles__0` | first run | your mobile, e.g. `+919xxxxxxxxx` |
| `NIJMITRA_Storage__RootPath` | recommended | `D:\NijMitraData\storage` (outside the site folder) |
| `NIJMITRA_Storage__MalwareScanner` | optional | `clamav` with `ClamAvHost`/`ClamAvPort` |

Set them as **system** environment variables (System Properties → Environment Variables) or per app pool (`appcmd set config -section:system.applicationHost/applicationPools /+"[name='NijMitra'].environmentVariables.[name='NIJMITRA_Jwt__SigningKey',value='…']"`). See `deployment/environment.production.example`.

Configurations per environment: **Development** (`appsettings.Development.json`, Swagger on, localhost CORS, auto-generated dev keys in `App_Data/dev-secrets.json`), **Testing** (`appsettings.Testing.json`, background jobs off, relaxed rate limits — used by the test suite), **Production** (`appsettings.Production.json`, Swagger off, HTTPS + HSTS, keys mandatory).

## 4. IIS site
1. Application pool `NijMitra`: *No Managed Code*, Integrated, Start mode *AlwaysRunning*, Idle time-out **0** (background jobs send reminders), identity `ApplicationPoolIdentity`.
2. Site `NijMitra` → physical path `C:\inetpub\nijmitra` (copy the publish output), bindings `https nijmitra.com:443` and `https www.nijmitra.com:443` (+ `http :80` for redirect).
3. Give the pool identity **Modify** rights on `C:\inetpub\nijmitra\App_Data`, `logs` and the storage path:
   `icacls C:\inetpub\nijmitra\App_Data /grant "IIS AppPool\NijMitra:(OI)(CI)M"`.
4. In *Configuration Editor* → `system.webServer/applicationInitialization` you may enable *preload* so jobs start after reboots.

## 5. Database migrations
Migrations run automatically at startup (`Database:AutoMigrate=true`). To run them explicitly (recommended before switching traffic):
```powershell
cd C:\inetpub\nijmitra
dotnet NIJMITRA.API.dll --migrate
```
Developers create new migrations with `dotnet ef migrations add <Name> -p NIJMITRA.Infrastructure -s NIJMITRA.Infrastructure -o Persistence/Migrations`.

## 6. HTTPS / SSL
* Obtain a certificate for `nijmitra.com` and `www.nijmitra.com` (e.g. win-acme for Let's Encrypt: `wacs.exe --target iis --siteid <id> --installation iis`, auto-renews), or import a commercial certificate (.pfx) into *Server Certificates*.
* Bind it on 443. `web.config` redirects HTTP→HTTPS and www→apex and sends HSTS. HTTPS is required for PWA install, service worker and push.
* If IIS sits behind a load balancer / proxy that terminates TLS, forward `X-Forwarded-Proto` (forwarded headers are enabled).

## 7. Verify
* `https://nijmitra.com/health` → `Healthy`.
* Landing page loads; register; sign in; Admin → Dashboard shows *System health: Healthy*.
* Admin → Communication Center: configure email/SMS/push and send tests.

## 8. Backup & restore
* `deployment/scripts/backup-database.ps1` (Windows Task Scheduler, daily 02:30) → `pg_dump` custom-format dump + ZIP of `App_Data` (files, encrypted DB-connection file); keeps 30 days. Linux: `backup-database.sh`.
* Store backups off-server, and store the **data-encryption key** separately (password manager / vault).
* Restore: stop the site → `restore-database.ps1 -DumpFile db-….dump -FilesZip files-….zip` → start the site.

## 9. Updating
Publish to a new folder, stop the app pool, swap folders (keep `App_Data`, `logs`), start the pool. Users get an *Update available* prompt from the PWA.

## 10. Troubleshooting
| Symptom | Fix |
|---|---|
| HTTP 500.30 / 502.5 at start | Event Viewer → Application; usually a missing `NIJMITRA_Jwt__SigningKey` / `Security__DataEncryptionKey` or DB connection. Temporarily set `stdoutLogEnabled="true"` in web.config. |
| `Failed to connect to database` | Check host/port/firewall, `pg_hba.conf`, password; test from Admin → Database (if reachable) or `psql`. |
| Chat/notifications not real-time | Enable the IIS **WebSocket Protocol** feature. |
| Reminders not sent | App pool idle time-out must be 0 and *AlwaysRunning*; check providers and Delivery logs. |
| PWA not installable | Must be HTTPS with a valid certificate; check `/manifest.webmanifest` and `/sw.js` load. |
| Uploads fail > 30 MB | `maxAllowedContentLength` in web.config and Admin → Platform settings → Files. |
| Users logged out quickly | Admin → Platform settings → Sessions: idle timeout is 60 s by default. |
| Logs | `C:\inetpub\nijmitra\logs\nijmitra-YYYYMMDD.log` (Serilog, 30 days). |

## Split hosting (optional)
Build the PWA with `VITE_API_BASE_URL=https://api.nijmitra.com`, deploy `dist` as a static site with `deployment/iis/web.config.static-frontend`, deploy the API on `api.nijmitra.com`, and set `NIJMITRA_Cors__AllowedOrigins__0=https://nijmitra.com`. Both are under the same registrable domain, so `SameSite=Strict` cookies keep working.
