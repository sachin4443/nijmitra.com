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
* **Screenshot protection:** a browser/PWA cannot block screenshots. NIJMITRA only applies deterrents (copy/context-menu blocking in chat, disappearing messages, privacy notices). Strong protection needs a native Android app using `FLAG_SECURE` — see the future-work section in the architecture doc.
* **Chat encryption:** messages are protected in transit (TLS) and access-controlled on the server; they are **not end-to-end encrypted**.
* **Password manager autofill:** a PWA cannot autofill other websites. A browser extension would be a separate project.
* **Vault recovery:** if a user forgets the master password *and* loses the recovery key, vault contents cannot be recovered (zero-knowledge).
* **Global search on vault:** only non-secret metadata (title, website, category, tags) is searchable; usernames are encrypted client-side and therefore not searchable server-side.
* **Diary search:** diary content is encrypted at rest; search covers titles, tags and moods, not the body text.

## Other notes
* **Database:** implemented for PostgreSQL (owner's choice). SQL Server would need a second migration set.
* **Logo SVGs:** the supplied logo is raster artwork, so `logo*.svg` embed the transparent PNG. The favicon is a true vector redraw. A designer-made vector master is recommended for print.
* **FullCalendar** is pinned to v6.1 (v7 is a breaking redesign).
* **UI language:** the interface is English with Hindi headings/taglines on key screens; a full Hindi translation is not yet implemented (the language preference is stored).
* **Not yet done:** load/performance testing, CI pipeline, real-device testing on iOS/Android, penetration test by a third party.
* **Provider APIs (2Factor.in, MSG91, Twilio, SendGrid, SES, WhatsApp):** implemented from their public API documentation but not exercised against live accounts in this environment — please run *Test connection* and *Send test* after adding credentials.
