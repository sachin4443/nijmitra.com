# NIJMITRA — Development Progress

Status legend: ✅ implemented & tested · 🟡 implemented, needs external credentials to verify live · ⏳ in progress · ⬜ not started

| Phase | Scope | Status |
|---|---|---|
| 1 | Repository inspection, architecture, ERD, roadmap | ✅ |
| 2 | Clean Architecture solution, EF Core + PostgreSQL, migrations, Swagger, Serilog, global exception handling | ✅ |
| 3 | Branding (official NijMitra logo set), theme switcher, landing, login/registration, responsive layout, dashboard | ✅ |
| 4 | Authentication & security (registration, conditional OTP, PIN, optional TOTP 2FA, refresh rotation, idle timeout, audit) | ✅ |
| 5 | Personal finance | ✅ |
| 6 | Document vault & zero-knowledge password manager | ✅ |
| 7 | Custom tables & product register | ✅ |
| 8 | Contacts & Udhar | ✅ |
| 9 | Diary & calendar & reminders | ✅ |
| 10 | Communication center | ✅ · 🟡 live delivery needs your provider credentials |
| 11 | Friends & chat | ✅ |
| 12 | PWA | ✅ (install/push verified in Chromium; iOS/Android device testing pending) |
| 13 | Admin panel | ✅ |
| 14 | Testing & production readiness | ✅ build/tests/e2e · ⏳ deployment to nijmitra.com by owner |

## Log

### Phase 1 — Repository inspection (2026-11-17)
* Repository `sachin4443/nijmitra.com` was empty (no commits); work happens on branch `claude/upbeat-brown-qo4i4j`.
* Environment: .NET SDK 10.0.115 (installed from Ubuntu packages), Node 22, PostgreSQL 16 on port 5431.
* Requirements adjusted per owner instructions: PostgreSQL instead of SQL Server; mobile OTP and email verification
  only activate after the admin configures SMS/email providers; two-step verification is user-optional (admin may enforce);
  theme switcher; admin-editable DB connection; Super-Admin "sign in as user"; client-facing PDF user guide.

### Phase 2 + 4 (backend) — Foundation & authentication
* Solution `NIJMITRA.sln` with Domain / Application / Infrastructure / API / Tests projects.
* ~80 entities, single `InitialCreate` migration, pg_trgm search indexes, owner & soft-delete global filters.
* Auth: registration (optional OTP), login with lockout, TOTP 2FA + recovery codes, refresh-token rotation with reuse detection,
  server-side idle timeout, sessions/devices, password/PIN change, re-authentication, password reset, email verification.
* Notification engine with encrypted outbox, dispatcher, 9 provider integrations, default policies & templates.
* Verified manually: register → login → /me → refresh (CSRF header enforced) → 2FA enrol → 2FA login (bad code rejected).

### Phases 5–11, 13 (backend)
* Finance (expenses, income, categories, budgets + overspend alerts, recurring bills with pending/paid/overdue, reports, PDF/Excel/CSV export).
* Document vault (folders, versioning, tags, preview/download, expiring share links with download limits).
* Zero-knowledge password vault API (ciphertext-only storage, history, secure notes, encrypted backup/import).
* Custom table builder (16 field types incl. formula, validation, unique, safe schema changes with data-loss confirmation, CSV/Excel import, Excel/CSV/PDF export).
* Product registers (custom fields, expiry filters, import/export), contacts (groups, attachments, 360° summary), Udhar ledger (partial payments, statements, reminders).
* Diary (sanitised, encrypted at rest), calendar (recurring events, tasks, reminders), reminder planner with dedupe keys.
* Global search across 12 modules, live dashboard aggregates.
* Friends & chat (invite codes, NM-ID search, requests, block/report, read/delivery receipts, typing, presence, disappearing messages).
* Admin: dashboard stats, user management, RBAC, impersonation, settings, branding, DB connection editor, audit logs, reports;
  communication center (providers, policies, templates, logs, announcements).
* Background jobs: notification dispatcher, reminders, presence sweep, disappearing messages, idle-session closer, retention & account purge.
* **Tests: 53 passing** (unit + integration against PostgreSQL) covering all 17 critical security scenarios.

### Phase 3, 12, 13 (frontend) & 14
* React 19 + TypeScript + Vite PWA with all 14 menu sections, admin portal, light/dark/system theme switcher, idle-timeout warning.
* Official NijMitra artwork processed into transparent logos (light/dark), vector favicon, PWA/Apple icons and OG image; portal re-themed to the logo palette (navy #0B3D91, orange #F47B20, green #3AAA35).
* Public landing page (Hindi + English) with features, security, platforms and Login/Register.
* Browser end-to-end runs (Playwright): registration, login, every module page, forms (expense, vault setup + credentials, custom table builder + records, Udhar partial payment, diary editor), chat between two users, admin pages, impersonation round-trip, mobile viewport, single-site production build under strict CSP — no console or server errors.
* Bugs found and fixed through E2E: impersonation cookie scope, diary archive & dashboard query translation, hub disconnect cancellation, expense form category race, sign-out redirect.
* Tests: **53 .NET tests** (unit + integration, all 17 critical scenarios) and **3 Vitest** crypto tests — all passing.
* Deployment kit: IIS web.config (single-site and split), publish/backup/restore/secret-generation scripts; full docs set.
* Client user guide PDF (no code) in `docs/NijMitra-User-Guide.pdf` and served at `/help/NijMitra-User-Guide.pdf`.
