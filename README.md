<p align="center"><img src="NIJMITRA.Web/public/branding/logo-light.png" alt="NijMitra" height="110"></p>

# NijMitra — Aapka Personal Digital Saathi

**अपनी पूरी निजी दुनिया अब एक ही App में।** NijMitra is a secure personal digital-life management platform: finance & budgets, document vault,
zero-knowledge password manager, custom tables, product registers, contacts, Udhar ledger, diary, calendar & reminders,
friends & private chat, notifications, and a full administration portal — delivered as an installable PWA at **https://nijmitra.com**.

| | |
|---|---|
| Backend | ASP.NET Core 10 Web API · EF Core · PostgreSQL · SignalR · Clean Architecture |
| Frontend | React 19 + TypeScript + Vite · Bootstrap 5 · PWA (Workbox) |
| Security | PBKDF2 hashing · JWT + rotating refresh tokens · optional TOTP 2FA · server-side idle timeout · AES-256-GCM · client-side encrypted vault · RBAC · audit logs |

## Repository layout
```
NIJMITRA.sln
NIJMITRA.Domain / Application / Infrastructure / API   – backend (Clean Architecture)
NIJMITRA.Web                                             – React PWA (user app + /admin)
NIJMITRA.Tests                                           – xUnit unit + integration tests
docs/                                                    – architecture, database, API, security, admin, deployment, user guide (PDF)
deployment/                                              – IIS configs, publish / backup / restore / key scripts
```

## Quick start (development)
Prerequisites: .NET SDK 10, Node.js 20+, PostgreSQL 14+.
```bash
cp NIJMITRA.API/appsettings.Local.example.json NIJMITRA.API/appsettings.Local.json   # set your connection string & admin mobile
cd NIJMITRA.API && dotnet run            # http://localhost:5080 — migrates the DB, Swagger at /swagger
cd NIJMITRA.Web && npm install && npm run dev   # http://localhost:5173 (proxies /api and /hubs)
```
Register with the mobile number listed in `App:BootstrapSuperAdminMobiles` to become Super Admin.
Development keys are generated automatically into `NIJMITRA.API/App_Data/dev-secrets.json` (git-ignored). **No secrets are committed.**

## Tests
```bash
NIJMITRA_TEST_DB="Host=localhost;Port=5431;Username=postgres;Password=…" dotnet test NIJMITRA.Tests   # 53 tests, throw-away DB per run
cd NIJMITRA.Web && npx vitest run && npm run build
```

## Documentation
[Architecture](docs/PROJECT_ARCHITECTURE.md) · [Database design](docs/DATABASE_DESIGN.md) · [ERD](docs/DATABASE_ERD.md) · [API](docs/API_DOCUMENTATION.md) ·
[Authentication](docs/AUTHENTICATION_FLOW.md) · [Vault security](docs/PASSWORD_VAULT_SECURITY.md) · [Notifications](docs/NOTIFICATION_CONFIGURATION.md) ·
[Admin guide](docs/ADMIN_GUIDE.md) · [Deployment](docs/DEPLOYMENT_GUIDE.md) · [Git workflow](docs/GIT_WORKFLOW.md) · [Progress](docs/DEVELOPMENT_PROGRESS.md) ·
[Known issues](docs/KNOWN_ISSUES.md) · **[User guide (PDF)](docs/NijMitra-User-Guide.pdf)**

A product by **OneStopFullStop Solution**.
