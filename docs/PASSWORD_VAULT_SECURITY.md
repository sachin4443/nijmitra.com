# NijMitra — Password Vault Security Design

## Goal
Store website and app credentials so that **only the owner can read them** — not administrators, not the database, not the server.

## Is it zero-knowledge?
**Yes, for the secrets:** usernames, emails, passwords, notes, password history and secure notes are encrypted and decrypted only in the browser. The server stores opaque ciphertext and has no key that can decrypt it.
**No, for metadata:** the entry title, website URL, application name, category and tags are stored in plaintext so they can be listed and found by global search. Do not put secrets in those fields.

## Key hierarchy
| Item | Where it exists | Purpose |
|---|---|---|
| Master password | User's memory only | Unlocks the vault |
| KEK | Browser memory, momentarily | PBKDF2-HMAC-SHA256(master password, 16-byte random salt, **600 000** iterations) → AES-256-GCM key |
| Vault key | Browser memory while unlocked (non-extractable `CryptoKey`) | Random 256-bit AES-GCM key that encrypts every item |
| `wrapped_key` | Server | Vault key encrypted with the KEK |
| Recovery key | Shown once to the user (24 characters) | PBKDF2 (200 000 iterations) → second KEK |
| `recovery_wrapped_key` | Server | Vault key encrypted with the recovery KEK |

Every item is AES-256-GCM with a fresh random 96-bit IV, serialised as `v1.<iv>.<ciphertext+tag>` (base64). GCM authentication means tampered data or a wrong master password is detected.

## Flows
* **Setup:** the browser generates the vault key, salts and recovery key, uploads only the two wrapped keys + KDF parameters.
* **Unlock:** browser derives the KEK and unwraps the vault key. A wrong password fails GCM authentication — nothing is sent to the server.
* **Auto-lock:** the key is dropped after 5 minutes without vault use, 1 minute after the tab is hidden, on sign-out and on session timeout.
* **Change master password:** re-wraps the same vault key (items are not re-encrypted); requires PIN re-authentication.
* **Forgot master password:** recovery key → unwrap → set new master password → new recovery key issued. **Without both, data cannot be recovered** — by design.
* **Password history:** when a password changes, the previous password (encrypted) is kept (last 20).
* **Backup:** encrypted JSON export (requires PIN). Import accepts a NijMitra backup (asks for that backup's master password, re-encrypts with the current vault key) or a CSV from Chrome/Edge/Bitwarden (`name,url,username,password,note`), encrypted in the browser before upload.

## Server-side guarantees
* Only ciphertext-shaped payloads are accepted (`[A-Za-z0-9+/=_\-.:]`, ≤ 64 KB) — plaintext JSON is rejected.
* Owner query filter + explicit checks: another user receives `404`.
* Vault endpoints are blocked for admin impersonation sessions; admin screens show only an entry **count**.
* Audit entries record *that* an entry was created/changed — never its content.
* Copying a secret clears the clipboard after 30 seconds (best effort, browser permitting).

## Limitations (honest)
* A PWA cannot auto-fill passwords into other websites. A browser extension would be a separate product.
* If the user's own device or browser is compromised (malware, malicious extension), secrets can be captured while the vault is unlocked — no web app can prevent that.
* Metadata (titles, URLs, categories, tags) is visible to the server.
