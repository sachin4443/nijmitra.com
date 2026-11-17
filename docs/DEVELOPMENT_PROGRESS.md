# NIJMITRA — Development Progress

Status legend: ✅ implemented & tested · 🟡 implemented, needs external credentials to verify live · ⏳ in progress · ⬜ not started

| Phase | Scope | Status |
|---|---|---|
| 1 | Repository inspection, architecture, ERD, roadmap | ✅ |
| 2 | Clean Architecture solution, EF Core + PostgreSQL, migrations, Swagger, Serilog, global exception handling | ✅ |
| 3 | Branding, theme, login/registration, responsive layout, dashboard | ⏳ |
| 4 | Authentication & security (registration, optional OTP, PIN, TOTP 2FA, refresh rotation, idle timeout, audit) | ✅ backend · ⏳ UI |
| 5 | Personal finance | ⬜ |
| 6 | Document vault & password manager | ⬜ |
| 7 | Custom tables & product register | ⬜ |
| 8 | Contacts & Udhar | ⬜ |
| 9 | Diary & calendar | ⬜ |
| 10 | Communication center | ⏳ engine + providers done |
| 11 | Friends & chat | ⬜ |
| 12 | PWA | ⬜ |
| 13 | Admin panel | ⬜ |
| 14 | Testing & production readiness | ⬜ |

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
