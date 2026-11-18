# NijMitra — Administrator Guide

## Becoming the first Super Admin
1. Put your mobile number in `App:BootstrapSuperAdminMobiles` (production: env var `NIJMITRA_App__BootstrapSuperAdminMobiles__0=+91XXXXXXXXXX`).
2. Register with that number (or, if already registered, sign in again). Alternative: `dotnet NIJMITRA.API.dll --promote-admin +91XXXXXXXXXX`.
3. Open **Admin Panel** from the sidebar or the profile menu.

## Roles
| Role | Can |
|---|---|
| Super Admin | everything, including roles, database connection and *Login as user* |
| Admin | users, settings, communication center, announcements, audit, reports |
| Support Admin | view dashboard, users, login history, communication logs, audit logs; handle reports |
Custom roles can be created under **Roles & permissions** (12 granular permissions).

## Areas
* **Dashboard** — users (total, active, new, suspended, 2FA adoption, online now), login activity, storage, notification & delivery statistics, queue health, database health.
* **Users** — search/filter, click a name to open the user. Actions: activate, deactivate, suspend (with reason, user notified), unlock, reset password (temporary password shown once), reset 2FA, end all sessions, manage roles, **Login as user** (audited; vault, chat and security settings stay locked; *Return to admin* in the red banner). Admins see only counts of a user's records — never vault items, diary text, chat messages or document files.
* **Communication Center** — providers, notification control matrix, templates, delivery logs, announcements (see NOTIFICATION_CONFIGURATION.md).
* **Platform settings** — application name/taglines/support email, custom logo & favicon, auto-logout timeout (default 60 s) and warning, maximum session length, lockout rules, *Require 2FA for everyone*, registration (open/closed, require mobile OTP, require verified email — only possible with an active provider), file size / quota / allowed types, maintenance mode, chat on/off, deletion grace period.
* **Website & footer** (Platform settings tab) — the public home-page footer: about text (English + optional Hindi), footer columns and links (add / rename / reorder / remove; only `https://`, `mailto:`, `tel:`, `/page` or `#section` links are accepted), contact email / phone / address, social-media links, © name and an optional company line, the Google Play link, and the **Android app** package name + SHA-256 signing fingerprints published at `/.well-known/assetlinks.json`. The privacy, terms and delete-account pages show the contact email set here.
* **Database** — view the current PostgreSQL connection (password never shown), test a new connection, then *Save & switch* (optionally running migrations). The new value is stored encrypted in `App_Data/database.json` on the server and used immediately.
* **Audit logs** — activity, logins, admin actions with IP, browser, OS and device.
* **User reports** — reports submitted from chat; mark reviewed / action taken / dismissed.

## Good practice
* Enable 2FA on every admin account.
* Use *Login as user* only with the user's consent for support; it is always recorded.
* Back up the database and `App_Data` daily (see DEPLOYMENT_GUIDE.md) and keep the data-encryption key safe.
