# NIJMITRA — Project Architecture

> **NIJMITRA** — *Your Life. Your Records. Your Control.* · निजी जीवन का डिजिटल साथी।

## 1. System overview

```
 ┌──────────────────────────── Browser / Installed PWA ────────────────────────────┐
 │ React 19 + TypeScript + Vite · Bootstrap 5 · React Router · Axios · SignalR     │
 │ Service worker (app-shell only, never caches /api) · WebCrypto (vault)           │
 └──────────────┬───────────────────────── HTTPS ──────────────────────┬────────────┘
                │ REST /api/* (JWT bearer, 10 min)                       │ WSS /hubs/app
                │ HttpOnly refresh cookie (path /api/auth, SameSite)     │ (SignalR)
 ┌──────────────▼────────────────────────────────────────────────────────▼──────────┐
 │ NIJMITRA.API (ASP.NET Core 10)                                                   │
 │  Controllers · AppHub · Middleware: exceptions → security headers → CORS →       │
 │  rate limiter → JWT auth → SessionValidation (idle timeout) → Maintenance → RBAC  │
 │  Background jobs: notification dispatcher, reminder planner, recurring bills,     │
 │  presence sweeper, disappearing messages, purge/retention                        │
 ├──────────────────────────────────────────────────────────────────────────────────┤
 │ NIJMITRA.Application  — use cases / services, DTOs, validation, notification     │
 │                         engine, provider contracts (no framework dependencies     │
 │                         except EF Core abstractions)                              │
 ├──────────────────────────────────────────────────────────────────────────────────┤
 │ NIJMITRA.Infrastructure — EF Core (PostgreSQL/Npgsql), migrations, AES-GCM field │
 │   encryption, PBKDF2 hashing, JWT, TOTP/QR, private file storage, ClamAV, Excel/ │
 │   CSV/PDF export, SMTP/SendGrid/SES, 2Factor/MSG91/Twilio/Custom HTTP SMS,        │
 │   Web Push (VAPID), WhatsApp Cloud API                                           │
 ├──────────────────────────────────────────────────────────────────────────────────┤
 │ NIJMITRA.Domain — entities, enums, constants (no dependencies)                   │
 └──────────────────────────────────────────────────────────────────────────────────┘
                │                                       │
         PostgreSQL 16 (Npgsql)                 Private file storage (App_Data/storage,
                                                outside web root, random keys)
```

Dependency rule: `Domain ← Application ← Infrastructure ← API`. Controllers are thin; every business
rule lives in an Application service.

## 2. Key design decisions

| Topic | Decision | Why |
|---|---|---|
| Database | **PostgreSQL** via Npgsql + EF Core Code-First migrations, snake_case naming | The owner's environment uses PostgreSQL (`Host=localhost;Port=5431;Database=nijmitraapp`). SQL Server support can be added with a second migration set if ever required. |
| Admin-editable DB connection | Encrypted `App_Data/database.json`, editable from **Admin → Database** with *Test Connection*; applied to new requests instantly | A connection string cannot live *only* inside the database it points to. |
| Data isolation | Every personal entity implements `IOwnedEntity`; a global EF **Owner** query filter (`UserId == current user`) plus write-time ownership checks | IDOR protection by default — a forgotten `Where` can't leak another user's rows. |
| Soft delete | Global **SoftDelete** filter on `ISoftDelete` entities | Recoverability + retention jobs. |
| Identity | Custom identity: PBKDF2-SHA512 (210k iterations) for password & PIN, JWT access tokens (10 min), opaque refresh tokens (hashed, rotated, reuse detection) in HttpOnly cookie | Full control over mobile-number login, PIN and session rules. |
| MFA | TOTP RFC 6238 (Google/Microsoft Authenticator), 10 one-time recovery codes; **optional per user**, can be **enforced platform-wide** by admin | Owner's requirement: users choose; admin can mandate later. |
| Idle timeout | Default **60 s**, admin-configurable. Client tracks genuine input + warning dialog; server rejects requests after `timeout + 5 s` of inactivity and revokes refresh tokens. Passive polling (`X-Activity: passive`) doesn't count. | Enforced on both sides. |
| Registration OTP / email verification | Off until an SMS / email provider is configured and the admin enables the toggle | Owner's requirement. |
| Password vault | **Client-side** AES-256-GCM, key from PBKDF2-SHA256 (600k) of the master password; server stores ciphertext only | Genuine zero-knowledge for secrets — admins and the server cannot read them. |
| Server-side secrets | AES-256-GCM `IFieldEncryptor` (TOTP secrets, provider API keys, diary content, queued message bodies) with key from env var | Protects DB dumps/backups. |
| Custom tables | Metadata + typed EAV (`custom_table_values` with text/number/date/bool columns) — **no physical table per user table** | Safe, migratable, indexable. |
| Notifications | Outbox (`notification_queue`) + background dispatcher with priority failover, retries, dedupe keys, delivery logs | Works when the browser is closed; never double-sends. |
| Real-time | One SignalR hub (`/hubs/app`), per-user groups | Chat, typing, presence, in-app notifications. |
| Admin impersonation | Super Admin only, audited, user notified, red banner; vault, chat and security settings blocked | Support access without breaking zero-knowledge or third-party chat privacy. |

## 3. Solution layout

```
NIJMITRA.sln
NIJMITRA.Domain/          Entities (Identity, Finance, Documents, Vault, Registers, People, Journal, Communication, Chat)
NIJMITRA.Application/     Auth, Account, Files, Notifications, Finance, Documents, Vault, CustomTables, Products,
                          Contacts, Udhar, Diary, Calendar, Search, Dashboard, Chat, Admin services
NIJMITRA.Infrastructure/  Persistence (DbContext, model config, migrations, seeder, DB connection store),
                          Security, Storage, Notifications (providers + dispatcher), Services (export, audit…)
NIJMITRA.API/             Program.cs, Controllers, Hubs, Middleware, BackgroundJobs, appsettings*.json
NIJMITRA.Web/             React + TypeScript PWA (user app + /admin portal)
NIJMITRA.Tests/           xUnit unit + integration tests (real PostgreSQL)
docs/                     Architecture, database, API, security, admin & deployment guides
deployment/               IIS web.config samples, publish scripts, backup scripts
```

## 4. Request lifecycle (authenticated)

1. JWT validated (issuer, audience, signature, lifetime).
2. `SessionValidationMiddleware` loads the session row → rejects revoked/expired, enforces idle timeout, updates `last_activity_at` for genuine activity (also the parent admin session during impersonation).
3. `[HasPermission]` policies check granular RBAC claims on admin endpoints.
4. Service executes; EF filters scope every query to the caller.
5. `AuditService` records security-relevant events (no secrets ever logged).

## 5. Security boundaries summary

* Server never trusts a client-supplied `UserId`. Identity comes from validated claims only.
* Uploaded files: allow-list → size/quota → magic-byte signature → optional ClamAV → random storage key; downloads only through authorised endpoints.
* Exports neutralise spreadsheet formula injection.
* HTML (diary) sanitised server-side with HtmlSanitizer.
* Logs: Serilog request logging prints method/path/status only; share-link tokens redacted; provider errors scrubbed of URLs and digit sequences.
