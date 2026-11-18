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

### Phase 15 — Feedback round (2026-11-18)
* **Chat:** messages appear instantly (optimistic send, background delivery with retry), photos are resized in the browser (an 11 MB photo → ~1.3 MB, sent in about a second) with an upload progress bar, uploads run in parallel, own images are shown from the local copy, typing events are throttled and a typing bubble is shown, the conversation stays pinned to the newest message (also when images finish loading), a *New messages* button appears when reading older messages, and the chat layout is fixed-height so the message box is always visible (desktop and mobile, `100dvh`, keyboard-aware).
* **Website language:** English by default; Hindi automatically when the device's first language is Hindi; language menu on the home and sign-in pages (remembered per device). The home page has an English hero built from a real app screen; the Hindi posters are shown in Hindi mode.
* **Branding:** “OneStopFullStop” removed from the website, legal pages, README and the PDF guide (poster cropped). The footer, including an optional company line, is now managed in **Admin → Platform Settings → Website & footer**.
* **Documents:** tap a document to open it inside the app — pdf.js viewer (works on Android/iOS, page-by-page, zoom), images with zoom, Excel/CSV as tables with sheet tabs, Word as text — with download only on demand. Server-side preview via `?view=preview` on any file endpoint (same authorization as the download; plain text output only).
* **Google Play:** Play-compliant privacy policy, public account-deletion page (`/delete-account.html`), one-year retention for sign-in/activity/delivery logs, `/.well-known/assetlinks.json` managed from Admin.
* **Android app:** `android/` Trusted Web Activity project (android-browser-helper 2.7.4, target SDK 36), adaptive + themed icon, splash, notification delegation, verified links; signed release `.aab` and `.apk` built; Play listing texts (EN/HI), Data-safety answers, icon, feature graphic and 7 phone screenshots in `android/play-store/`.
* **Bug fixed:** when the server expired *one* idle session (e.g. a phone), every other device of the same user was signed out too. Clients now only sign out for their own session id; opening/reloading the app also syncs the server's idle clock.
* Tests: 60 backend tests (new: Office preview, footer validation + assetlinks, preview authorization) · 3 frontend tests · 34-step browser E2E for this round (all pass).
