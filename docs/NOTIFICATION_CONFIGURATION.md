# NijMitra — Notification & Communication Configuration

## Architecture
```
Feature (reminder, login, OTP, chat…) → NotificationService.Dispatch(event)
   1. Platform policy (Admin → Communication Center → Notification control matrix)
   2. Active provider exists for the channel?
   3. User preference (channel + category) — skipped for security-critical events
   → In-app notification (DB + real-time SignalR) and/or encrypted outbox rows (notification_queue)
Background NotificationDispatcher (every 5 s): providers in priority order (failover) → CommunicationLog per attempt
   success → body wiped · failure → retry with exponential backoff (30 s, 60 s, 120 s…) → Dead after max attempts
```
* **Duplicate prevention:** every queue row has a unique dedupe key; reminders use deterministic keys per occurrence & offset.
* **Browser independence:** reminders are planned (`reminder_schedules`) and sent by background jobs; the user's browser does not need to be open.
* **Privacy:** recipient addresses and bodies are AES-GCM encrypted in the queue; logs keep only masked recipients; provider errors are scrubbed of URLs and numbers (so OTPs/keys never land in logs).

## Supported providers
| Channel | Providers | Notes |
|---|---|---|
| Email | SMTP / custom SMTP (MailKit), SendGrid (v3 API), Amazon SES (v2 API) | Test connection = SMTP auth / API key check |
| SMS | 2Factor.in, MSG91 (Flow v5), Twilio, **Custom HTTP API** | Custom: URL, method, JSON/form body template, headers, auth (Bearer/Basic/Header/Query), placeholders `{{mobile}} {{mobileNoPlus}} {{mobile10}} {{message}} {{otp}} {{senderId}} {{templateId}} {{apiKey}} {{apiSecret}}`, optional “success response contains” |
| Push | Web Push with VAPID (browsers & installed PWA) | Generate keys with one click; users enable push per device |
| WhatsApp | WhatsApp Business Cloud API (templates) | **Not operational until configured and a test succeeds** |

Secrets are AES-256-GCM encrypted in `communication_providers.encrypted_secrets_json` and are never returned to the browser; leave a secret field blank when editing to keep it.

### India (DLT)
Commercial SMS in India requires DLT registration. Configure your approved **Sender ID** on the provider and the **DLT / provider template id** on each SMS template (Admin → Communication Center → Templates → SMS).

## Control matrix defaults
| Event | Email | SMS | Push | In-App | Notes |
|---|---|---|---|---|---|
| Registration OTP | ✔ | ✔ | – | ✔ | security-critical |
| Email verification | ✔ | – | – | ✔ | security-critical |
| Password reset | ✔ | ✔ | – | ✔ | security-critical |
| Login alert | ✔ | – | ✔ | ✔ | |
| Security change | ✔ | – | ✔ | ✔ | security-critical |
| Meeting / expense / general reminder | ✔ | – | ✔ | ✔ | |
| Udhar reminder | ✔ | ✔ | ✔ | ✔ | |
| Friend request | ✔ | – | ✔ | ✔ | |
| New chat message | – | – | ✔ | ✔ | push only when the friend is offline, max 1 per conversation per 10 min |
| Account suspension, admin access | ✔ | ✔/– | – | ✔ | security-critical |

Security-critical events: in-app always on, **Email or SMS must stay enabled** (the matrix refuses otherwise), users cannot opt out.

## Templates
Editable per event and channel with placeholders `{{UserName}} {{OTP}} {{ApplicationName}} {{EventDate}} {{EventTime}} {{Title}} {{Body}} {{Link}} {{ExpiryMinutes}} {{Browser}} {{IpAddress}} {{Location}}`. HTML emails are wrapped in the branded layout; values are HTML-encoded. Live preview and *Send test* are available.

## Go-live checklist
1. Add an email provider → *Test connection* → *Send test*.
2. Add an SMS provider (DLT sender + template ids) → test → then optionally enable *Require mobile OTP* in Platform settings.
3. Add Web Push → *Generate VAPID keys* → save → enable push on your device → *Send test*.
4. Review the matrix and templates; watch **Delivery logs**.
