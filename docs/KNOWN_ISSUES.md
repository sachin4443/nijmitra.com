# NIJMITRA — Known Issues & Limitations

This file is kept honest: anything not fully verified is listed here.

## External integrations (need your credentials to verify live)
| Integration | Status | What is needed |
|---|---|---|
| SMTP / SendGrid / Amazon SES | Implemented; SMTP path testable with any mailbox | Host/port/user/password or API key, verified sender |
| 2Factor.in / MSG91 / Twilio / Custom HTTP SMS | Implemented against public API docs; not exercised against live accounts | API key, DLT-approved sender ID and template IDs (India) |
| Web Push (VAPID) | Implemented; keys can be generated in the admin panel | Nothing external — HTTPS origin required in production |
| WhatsApp Business Cloud API | Interface + implementation present; **not operational** until a verified number, token and approved templates are configured and tested | Meta Business verification, phone number ID, permanent token |
| ClamAV malware scanning | Implemented (clamd INSTREAM); disabled by default (`Storage:MalwareScanner=none`) | A reachable clamd service |

## Product limitations (by design / platform)
* **Screenshot protection:** a browser/PWA cannot block screenshots, and neither can the Android app (a Trusted Web Activity renders through Chrome, so `FLAG_SECURE` is not available). NIJMITRA only applies deterrents (copy/context-menu blocking in chat, disappearing messages, privacy notices). Strong protection would need a fully native screen for chat.
* **Chat encryption:** messages are protected in transit (TLS) and access-controlled on the server; they are **not end-to-end encrypted**.
* **Password manager autofill:** a PWA cannot autofill other websites. A browser extension would be a separate project.
* **Vault recovery:** if a user forgets the master password *and* loses the recovery key, vault contents cannot be recovered (zero-knowledge).
* **Global search on vault:** only non-secret metadata (title, website, category, tags) is searchable; usernames are encrypted client-side and therefore not searchable server-side.
* **Diary search:** diary content is encrypted at rest; search covers titles, tags and moods, not the body text.

## Other notes
* **Database:** implemented for PostgreSQL (owner's choice). SQL Server would need a second migration set.
* **Logo SVGs:** the supplied logo is raster artwork, so `logo*.svg` embed the transparent PNG. The favicon is a true vector redraw. A designer-made vector master is recommended for print.
* **FullCalendar** is pinned to v6.1 (v7 is a breaking redesign).
* **UI language:** the public website (home, login, register, forgot password) is fully available in English and Hindi with automatic detection and a language menu. Inside the signed-in app the interface is English (with Hindi headings on some screens); a full Hindi translation of every module is not yet done.
* **Document preview:** PDFs, images, audio/video, text, `.xlsx`, `.csv` and `.docx` open inside the app. Word/Excel are shown as a readable text/table preview (no images or exact layout); legacy `.doc`/`.xls` must be downloaded. Previews are limited to 20 MB, 500 rows × 40 columns per sheet.
* **Android app:** built and signed here, verified with `jarsigner`/`apksigner`, but not yet run on a physical device or emulator. It needs Chrome (or another browser supporting Trusted Web Activities); otherwise it opens in a Custom Tab with a URL bar. It shows the URL bar until `assetlinks.json` lists the Google Play app-signing fingerprint (Admin → Website & footer).
* **Not yet done:** load/performance testing, CI pipeline, real-device testing on iOS/Android (the Android app has not been installed on a phone yet), penetration test by a third party.
* **Provider APIs (2Factor.in, MSG91, Twilio, SendGrid, SES, WhatsApp):** implemented from their public API documentation but not exercised against live accounts in this environment — please run *Test connection* and *Send test* after adding credentials.
