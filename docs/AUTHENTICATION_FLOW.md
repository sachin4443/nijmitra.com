# NijMitra — Authentication & Session Flow

## Registration
1. `GET /api/auth/registration-config` tells the UI whether registration is open and whether mobile OTP is required.
2. **Mobile OTP is required only when** the admin has switched on *Require mobile OTP* **and** an SMS provider is active (the admin cannot enable the switch without an active provider; if the last SMS provider is disabled the switch turns itself off so users are never locked out).
3. `POST /api/auth/register` — full name, mobile, password (8–128 chars, 3 of upper/lower/digit/symbol), PIN (4–6 digits, not trivial), terms. Passwords and PINs are hashed with PBKDF2-HMAC-SHA512 (210 000 iterations, ASP.NET Core Identity v3 format).
4. The user receives a public **NijMitra ID** (e.g. `NM100025`) and default categories/groups. Mobile numbers listed in `App:BootstrapSuperAdminMobiles` become Super Admin.

## Email
Users add an email in Settings → Security. If an email provider is active, a 6-digit code is sent to the new address and the address becomes verified after confirmation (the old address is notified). If no email provider exists yet, the email is saved unverified and verification starts once the admin configures email.

## Sign-in
```
POST /api/auth/login {mobile, password, deviceKey}
 ├─ wrong password  → 401 invalid_credentials (failed counter; lockout after N attempts for M minutes — admin-configurable)
 ├─ 2FA enabled     → { status: two_factor_required, challengeToken (5 min, bound to security stamp) }
 │      POST /api/auth/login/2fa {challengeToken, code | recovery code}
 ├─ 2FA enforced by admin but not set up → { status: two_factor_setup_required, challengeToken }
 │      POST /api/auth/login/2fa/setup → QR + manual key; POST /api/auth/login/2fa/enable → recovery codes + tokens
 └─ otherwise       → { status: success, accessToken } + HttpOnly refresh cookie
```
Two-step verification is **optional per user** (Settings → Security → *Set up authenticator*) unless the admin turns on *Require two-step verification for everyone*.

* **TOTP:** RFC 6238 (SHA-1, 6 digits, 30 s, ±1 step) — Google / Microsoft Authenticator. Secrets are AES-256-GCM encrypted at rest; a code (time-step) can be used only once (replay protection).
* **Recovery codes:** 10 single-use codes, stored as keyed HMAC hashes; regenerating invalidates old ones.

## Tokens & sessions
* **Access token:** JWT (HS256), 10 minutes, kept only in browser memory. Claims: `sub`, `sid` (session), `pid`, roles and `perm` claims (none during impersonation), `imp` when impersonating.
* **Refresh token:** 48 random bytes, stored only as an HMAC hash, sent as `nm_rt` cookie (`HttpOnly`, `Secure` in production, `SameSite=Strict`, `Path=/api/auth`). Every refresh **rotates** the token; presenting an already-rotated token revokes the whole session (theft detection). Refresh requires the `X-Requested-With: NIJMITRA` header (CSRF defence).
* **Sessions** (`user_sessions`) record IP, browser, OS and device. Users can see active sessions, end any of them, sign out all other devices and remove known devices.

## Automatic logout (default 60 s, admin-configurable)
* **Browser:** genuine input (mouse, keyboard, touch, wheel, scroll) resets the timer; activity is shared between tabs; a warning with countdown appears before the end; on timeout the client calls logout, clears memory, the vault key and session storage, and returns to the login page.
* **Server:** every authenticated request checks `last_activity_at`. If idle longer than the timeout (+5 s tolerance) the session and its refresh tokens are revoked and `401 session_expired` is returned. Passive polling (`X-Activity: passive`) does not extend the session. A background job also closes idle sessions whose browser disappeared, and SignalR hub calls are rejected for expired sessions.

## Password, PIN and re-authentication
* Change password (current password required) revokes all other sessions. Admin "reset password" issues a temporary password and forces a change at next sign-in.
* Self-service password reset sends an OTP by SMS (or email) — available only once a provider is configured; responses never reveal whether an account exists.
* Sensitive operations require a **fresh PIN or password confirmation** (valid 5 minutes for the session): data export, vault backup, vault master-password change, account deletion.

## Admin "Login as user"
Super Admin only (`admin.users.impersonate`), from **Admin → Users → (user) → Login as user**:
* allowed only for active, non-admin accounts; the admin's own refresh cookie is parked and restored by **Return to admin**;
* the impersonated session carries **no admin permissions**; password vault, chat and all security settings return `403 impersonation_blocked`;
* every action is audited with the admin's id, an admin-audit entry is written, and the user receives an "Administrator account access" notification (can be turned off by the admin).

## Audit
`login_audit_logs` (sign-in, failures, lockouts, auto-logout, logout, refresh-token reuse), `audit_logs` (profile/security changes, record create/update/delete, file upload/download, exports) and `admin_audit_logs` (every admin action). Passwords, PINs, OTPs, authenticator secrets, vault contents and API secrets are never logged.
