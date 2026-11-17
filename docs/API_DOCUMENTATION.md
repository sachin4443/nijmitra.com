# NijMitra — API Documentation

Base URL: `https://nijmitra.com` (single-site) · Interactive docs: `/swagger` (enabled in Development, or with `Swagger:Enabled=true`).

## Conventions

* **Auth:** `Authorization: Bearer <access token>` (JWT, 10 minutes). Refresh via `POST /api/auth/refresh` using the HttpOnly `nm_rt` cookie plus header `X-Requested-With: NIJMITRA` (CSRF guard).
* **Ownership:** every personal endpoint is scoped to the signed-in user on the server; IDs belonging to other users return `404`.
* **Idle timeout:** requests after the configured inactivity window return `401` with `code: session_expired`. Background polling should send `X-Activity: passive`.
* **Errors:** RFC 7807 problem JSON: `{ status, title, code, errors?: { field: [message] }, traceId }`. Codes include `validation_failed`, `not_found`, `forbidden`, `conflict`, `reauth_required`, `impersonation_blocked`, `rate_limited`, `maintenance`.
* **Re-authentication:** sensitive operations (data export, vault backup / re-key, account deletion) answer `403 reauth_required`; call `POST /api/auth/reauthenticate` with `{ pin }` or `{ password }` and retry within 5 minutes.
* **Admin:** `/api/admin/*` require the matching permission claim (`admin.*`). Impersonated sessions can never call admin, vault, chat or security endpoints.
* **Real-time:** SignalR hub at `/hubs/app` (token via `access_token` query). Server → client events: `notification`, `message`, `messageDeleted`, `messageExpired`, `typing`, `read`, `delivered`, `presence`, `friendRequest`, `friendAccepted`, `conversationUpdated`, `sessionEnded`. Client → server: `Heartbeat()`, `Typing(conversationId, isTyping)`, `MarkRead(conversationId)`.

## Endpoints

### My account (/api/me)

| Method | Path |
|---|---|
| GET | `/api/me` |
| PUT | `/api/me/profile` |
| PUT | `/api/me/theme` |
| GET | `/api/me/dashboard-layout` |
| PUT | `/api/me/dashboard-layout` |
| POST | `/api/me/avatar` |
| GET | `/api/me/avatar/{userId}` |
| GET | `/api/me/notification-preferences` |
| PUT | `/api/me/notification-preferences` |
| POST | `/api/me/push-subscriptions` |
| DELETE | `/api/me/push-subscriptions` |
| POST | `/api/me/export` |
| POST | `/api/me/delete-account` |

### Administration

| Method | Path |
|---|---|
| GET | `/api/admin/dashboard` |
| GET | `/api/admin/users` |
| GET | `/api/admin/users/{id}` |
| GET | `/api/admin/users/{id}/login-history` |
| POST | `/api/admin/users/{id}/status` |
| POST | `/api/admin/users/{id}/unlock` |
| POST | `/api/admin/users/{id}/reset-password` |
| POST | `/api/admin/users/{id}/reset-2fa` |
| POST | `/api/admin/users/{id}/revoke-sessions` |
| PUT | `/api/admin/users/{id}/roles` |
| GET | `/api/admin/roles` |
| POST | `/api/admin/roles` |
| GET | `/api/admin/permissions` |
| PUT | `/api/admin/roles/{id}` |
| DELETE | `/api/admin/roles/{id}` |
| GET | `/api/admin/settings` |
| PUT | `/api/admin/settings` |
| POST | `/api/admin/branding/{kind}` |
| DELETE | `/api/admin/branding/{kind}` |
| GET | `/api/admin/audit` |
| GET | `/api/admin/reports` |
| POST | `/api/admin/reports/{id}` |

### Authentication (public)

| Method | Path |
|---|---|
| GET | `/api/auth/registration-config` |
| POST | `/api/auth/register/send-otp` |
| POST | `/api/auth/register` |
| POST | `/api/auth/login` |
| POST | `/api/auth/login/2fa` |
| POST | `/api/auth/login/2fa/setup` |
| POST | `/api/auth/login/2fa/enable` |
| POST | `/api/auth/refresh` |
| POST | `/api/auth/logout` |
| POST | `/api/auth/activity` |
| POST | `/api/auth/password-reset/request` |
| POST | `/api/auth/password-reset/confirm` |
| POST | `/api/auth/impersonate/{id}` |
| POST | `/api/auth/impersonation/end` |

### Calendar & reminders

| Method | Path |
|---|---|
| GET | `/api/calendar/events` |
| POST | `/api/calendar/events` |
| GET | `/api/calendar/events/{id}` |
| PUT | `/api/calendar/events/{id}` |
| DELETE | `/api/calendar/events/{id}` |
| POST | `/api/calendar/events/{id}/complete` |
| POST | `/api/calendar/events/{id}/attachments` |
| GET | `/api/calendar/upcoming` |
| GET | `/api/calendar/reminders` |
| POST | `/api/calendar/reminders` |
| PUT | `/api/calendar/reminders/{id}` |
| DELETE | `/api/calendar/reminders/{id}` |
| POST | `/api/calendar/reminders/{id}/complete` |

### Friends & chat

| Method | Path |
|---|---|
| GET | `/api/chat/users/{publicId}` |
| GET | `/api/chat/invitations` |
| POST | `/api/chat/invitations` |
| DELETE | `/api/chat/invitations/{id}` |
| POST | `/api/chat/invitations/accept` |
| GET | `/api/chat/requests` |
| POST | `/api/chat/requests` |
| POST | `/api/chat/requests/{id}/accept` |
| POST | `/api/chat/requests/{id}/reject` |
| DELETE | `/api/chat/requests/{id}` |
| GET | `/api/chat/friends` |
| DELETE | `/api/chat/friends/{userId}` |
| POST | `/api/chat/users/{userId}/block` |
| POST | `/api/chat/users/{userId}/report` |
| GET | `/api/chat/conversations/{id}/messages` |
| POST | `/api/chat/conversations/{id}/messages` |
| DELETE | `/api/chat/messages/{id}` |
| POST | `/api/chat/conversations/{id}/read` |
| PUT | `/api/chat/conversations/{id}/disappearing` |
| GET | `/api/chat/messages/{messageId}/files/{fileId}` |

### Administration — communication center

| Method | Path |
|---|---|
| GET | `/api/admin/communication/provider-definitions` |
| GET | `/api/admin/communication/providers` |
| POST | `/api/admin/communication/providers` |
| PUT | `/api/admin/communication/providers/{id}` |
| DELETE | `/api/admin/communication/providers/{id}` |
| POST | `/api/admin/communication/providers/{id}/active` |
| POST | `/api/admin/communication/providers/{id}/test-connection` |
| POST | `/api/admin/communication/providers/{id}/test-send` |
| POST | `/api/admin/communication/vapid-keys` |
| GET | `/api/admin/communication/policies` |
| PUT | `/api/admin/communication/policies` |
| GET | `/api/admin/communication/templates` |
| POST | `/api/admin/communication/templates` |
| PUT | `/api/admin/communication/templates/{id}` |
| DELETE | `/api/admin/communication/templates/{id}` |
| POST | `/api/admin/communication/templates/preview` |
| POST | `/api/admin/communication/templates/test` |
| GET | `/api/admin/communication/logs` |
| GET | `/api/admin/communication/queue` |
| GET | `/api/admin/communication/announcements` |
| POST | `/api/admin/communication/announcements` |

### Contacts

| Method | Path |
|---|---|
| GET | `/api/contacts/groups` |
| POST | `/api/contacts/groups` |
| PUT | `/api/contacts/groups/{id}` |
| DELETE | `/api/contacts/groups/{id}` |
| GET | `/api/contacts` |
| POST | `/api/contacts` |
| GET | `/api/contacts/{id}` |
| PUT | `/api/contacts/{id}` |
| DELETE | `/api/contacts/{id}` |
| GET | `/api/contacts/{id}/summary` |
| POST | `/api/contacts/{id}/avatar` |
| POST | `/api/contacts/{id}/attachments` |
| DELETE | `/api/contacts/{id}/attachments/{fileId}` |
| GET | `/api/contacts/export` |

### Custom tables

| Method | Path |
|---|---|
| GET | `/api/tables` |
| POST | `/api/tables` |
| GET | `/api/tables/{id}` |
| PUT | `/api/tables/{id}` |
| DELETE | `/api/tables/{id}` |
| POST | `/api/tables/{id}/preview-changes` |
| POST | `/api/tables/{id}/records/query` |
| GET | `/api/tables/{id}/records/{recordId}` |
| PUT | `/api/tables/{id}/records/{recordId}` |
| DELETE | `/api/tables/{id}/records/{recordId}` |
| POST | `/api/tables/{id}/records` |
| POST | `/api/tables/{id}/files` |
| POST | `/api/tables/{id}/records/{recordId}/attachments` |
| DELETE | `/api/tables/{id}/records/{recordId}/attachments/{fileId}` |
| GET | `/api/tables/{id}/export` |
| POST | `/api/tables/{id}/import` |

### Dashboard

| Method | Path |
|---|---|
| GET | `/api/dashboard` |

### Administration — database

| Method | Path |
|---|---|
| GET | `/api/admin/database` |
| PUT | `/api/admin/database` |
| POST | `/api/admin/database/test` |

### Diary

| Method | Path |
|---|---|
| GET | `/api/diary` |
| POST | `/api/diary` |
| GET | `/api/diary/archive` |
| GET | `/api/diary/dates/{year}/{month}` |
| GET | `/api/diary/tags` |
| GET | `/api/diary/{id}` |
| PUT | `/api/diary/{id}` |
| DELETE | `/api/diary/{id}` |
| POST | `/api/diary/{id}/attachments` |
| DELETE | `/api/diary/{id}/attachments/{fileId}` |

### Document vault

| Method | Path |
|---|---|
| GET | `/api/documents/categories` |
| GET | `/api/documents/folders` |
| POST | `/api/documents/folders` |
| PUT | `/api/documents/folders/{id}` |
| DELETE | `/api/documents/folders/{id}` |
| GET | `/api/documents` |
| POST | `/api/documents` |
| GET | `/api/documents/history` |
| GET | `/api/documents/{id}` |
| PUT | `/api/documents/{id}` |
| DELETE | `/api/documents/{id}` |
| POST | `/api/documents/{id}/versions` |
| GET | `/api/documents/{id}/versions` |
| POST | `/api/documents/{id}/move` |
| GET | `/api/documents/{id}/download` |
| POST | `/api/documents/{id}/shares` |
| GET | `/api/documents/{id}/shares` |
| DELETE | `/api/documents/shares/{shareId}` |

### Files

| Method | Path |
|---|---|
| GET | `/api/files/{id}` |

### Finance

| Method | Path |
|---|---|
| GET | `/api/finance/categories` |
| POST | `/api/finance/categories` |
| PUT | `/api/finance/categories/{id}` |
| DELETE | `/api/finance/categories/{id}` |
| GET | `/api/finance/expenses` |
| POST | `/api/finance/expenses` |
| GET | `/api/finance/expenses/{id}` |
| PUT | `/api/finance/expenses/{id}` |
| DELETE | `/api/finance/expenses/{id}` |
| GET | `/api/finance/expenses/export` |
| GET | `/api/finance/income` |
| POST | `/api/finance/income` |
| PUT | `/api/finance/income/{id}` |
| DELETE | `/api/finance/income/{id}` |
| GET | `/api/finance/income/export` |
| GET | `/api/finance/budget/{year}/{month}` |
| PUT | `/api/finance/budget` |
| GET | `/api/finance/budget/comparison` |
| GET | `/api/finance/recurring` |
| POST | `/api/finance/recurring` |
| PUT | `/api/finance/recurring/{id}` |
| DELETE | `/api/finance/recurring/{id}` |
| GET | `/api/finance/recurring/occurrences` |
| POST | `/api/finance/recurring/occurrences/{id}/pay` |
| POST | `/api/finance/recurring/occurrences/{id}/skip` |
| GET | `/api/finance/reports` |
| GET | `/api/finance/reports/export` |

### In-app notifications

| Method | Path |
|---|---|
| GET | `/api/notifications` |
| GET | `/api/notifications/unread-count` |
| POST | `/api/notifications/{id}/read` |
| POST | `/api/notifications/read-all` |
| DELETE | `/api/notifications/{id}` |

### Product registers

| Method | Path |
|---|---|
| GET | `/api/product-registers` |
| POST | `/api/product-registers` |
| PUT | `/api/product-registers/{id}` |
| DELETE | `/api/product-registers/{id}` |
| GET | `/api/product-registers/{id}/products` |
| POST | `/api/product-registers/{id}/products` |
| GET | `/api/product-registers/{id}/products/{pid}` |
| PUT | `/api/product-registers/{id}/products/{pid}` |
| DELETE | `/api/product-registers/{id}/products/{pid}` |
| POST | `/api/product-registers/images` |
| POST | `/api/product-registers/{id}/products/{pid}/attachments` |
| DELETE | `/api/product-registers/{id}/products/{pid}/attachments/{fileId}` |
| GET | `/api/product-registers/{id}/export` |
| POST | `/api/product-registers/{id}/import` |

### Public share links

| Method | Path |
|---|---|
| GET | `/api/public/share/{token}` |

### Global search

| Method | Path |
|---|---|
| GET | `/api/search` |
| GET | `/api/search/recent` |
| DELETE | `/api/search/recent` |

### Account security

| Method | Path |
|---|---|
| GET | `/api/auth/sessions` |
| DELETE | `/api/auth/sessions/{id}` |
| POST | `/api/auth/sessions/revoke-others` |
| GET | `/api/auth/devices` |
| DELETE | `/api/auth/devices/{id}` |
| GET | `/api/auth/login-history` |
| POST | `/api/auth/2fa/setup` |
| POST | `/api/auth/2fa/enable` |
| POST | `/api/auth/2fa/disable` |
| POST | `/api/auth/2fa/recovery-codes` |
| POST | `/api/auth/change-password` |
| POST | `/api/auth/change-pin` |
| POST | `/api/auth/reauthenticate` |
| POST | `/api/auth/email` |
| POST | `/api/auth/email/verify` |
| POST | `/api/auth/email/resend` |

### System (public)

| Method | Path |
|---|---|
| GET | `/api/system/info` |
| GET | `/api/system/branding/{kind}` |

### Udhar ledger

| Method | Path |
|---|---|
| GET | `/api/udhar` |
| POST | `/api/udhar` |
| GET | `/api/udhar/summary` |
| GET | `/api/udhar/{id}` |
| PUT | `/api/udhar/{id}` |
| DELETE | `/api/udhar/{id}` |
| POST | `/api/udhar/{id}/payments` |
| DELETE | `/api/udhar/{id}/payments/{paymentId}` |
| POST | `/api/udhar/{id}/attachments` |
| GET | `/api/udhar/statement` |
| GET | `/api/udhar/export` |

### Uploads

| Method | Path |
|---|---|
| POST | `/api/uploads` |

### Password vault (zero-knowledge)

| Method | Path |
|---|---|
| GET | `/api/vault` |
| POST | `/api/vault/setup` |
| PUT | `/api/vault/key` |
| GET | `/api/vault/categories` |
| GET | `/api/vault/entries` |
| POST | `/api/vault/entries` |
| GET | `/api/vault/entries/{id}` |
| PUT | `/api/vault/entries/{id}` |
| DELETE | `/api/vault/entries/{id}` |
| POST | `/api/vault/entries/{id}/favorite` |
| GET | `/api/vault/entries/{id}/history` |
| GET | `/api/vault/notes` |
| POST | `/api/vault/notes` |
| PUT | `/api/vault/notes/{id}` |
| DELETE | `/api/vault/notes/{id}` |
| POST | `/api/vault/export` |
| POST | `/api/vault/import` |

_Total: 263 endpoints. Generated from the OpenAPI document._