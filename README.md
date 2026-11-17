# NIJMITRA

**Your Life. Your Records. Your Control.** · निजी जीवन का डिजिटल साथी।

NIJMITRA is a secure personal digital-life management platform: finance, document vault, zero-knowledge password manager,
custom tables, product registers, contacts, Udhar ledger, diary, calendar & reminders, friends & private chat, notifications,
and a full administration portal — delivered as an installable PWA.

* Backend: ASP.NET Core 10 Web API · EF Core · PostgreSQL · SignalR · Clean Architecture
* Frontend: React + TypeScript + Vite · Bootstrap 5 · PWA

See [`docs/`](docs) for architecture, security and deployment guides. Development status: [`docs/DEVELOPMENT_PROGRESS.md`](docs/DEVELOPMENT_PROGRESS.md).

## Quick start (development)

Prerequisites: .NET SDK 10, Node 20+, PostgreSQL 14+.

```bash
cp NIJMITRA.API/appsettings.Local.example.json NIJMITRA.API/appsettings.Local.json   # then edit the connection string
cd NIJMITRA.API && dotnet run          # http://localhost:5080 (migrates DB automatically, Swagger at /swagger)
cd NIJMITRA.Web && npm install && npm run dev   # http://localhost:5173
```

Add your mobile number to `App:BootstrapSuperAdminMobiles` in `appsettings.Local.json` before registering to become Super Admin
(or run `dotnet NIJMITRA.API.dll --promote-admin <mobile>` later).

Secrets are **never** committed. Development keys are generated automatically into `NIJMITRA.API/App_Data/dev-secrets.json` (git-ignored).
