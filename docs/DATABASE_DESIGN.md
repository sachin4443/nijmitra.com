# NijMitra — Database Design

PostgreSQL 14+ via EF Core code-first migrations (`NIJMITRA.Infrastructure/Persistence/Migrations`). This document is generated from the live schema.

## Conventions

* snake_case names; `uuid` primary keys generated client-side (UUID v7, time-ordered); `timestamptz` in UTC; money `numeric(18,2)`, custom-table numbers `numeric(28,8)`.
* Personal tables carry `user_id` and are protected by a global **Owner** query filter (rows are only ever visible to their owner) plus write-time ownership checks — a client-supplied user id is never trusted.
* `is_deleted` / `deleted_at` = soft delete (global **SoftDelete** filter). Files are physically removed by the retention job after 30 days.
* `created_at`, `updated_at`, `created_by`, `updated_by` are stamped automatically.
* Encrypted columns (AES-256-GCM, server key): `user_authenticator_secrets.encrypted_secret`, `diary_entries.encrypted_content`, `communication_providers.encrypted_secrets_json`, `notification_queue.encrypted_*`. Vault columns `password_entries.encrypted_data`, `password_history.encrypted_password`, `secure_notes.encrypted_data` and the wrapped keys in `password_vaults` are encrypted **in the browser**; the server cannot decrypt them.
* Custom tables use typed EAV (`custom_table_values`) — no physical table is created per user table. The `pg_trgm` extension provides fast case-insensitive search indexes.
* Requested spec tables map as follows: `EmailProviderSettings` / `SmsProviderSettings` / `PushProviderSettings` → `communication_providers` (one row per provider, typed JSON settings); `CommunicationProviders` → same; `NotificationQueue` → `notification_queue`; `UserPresence` → `user_presences`.

## Identity & security

### `users`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| sequence_number | bigint |  |
| public_id | character varying |  |
| full_name | character varying |  |
| mobile_number | character varying |  |
| mobile_verified | boolean |  |
| email | character varying | yes |
| email_verified | boolean |  |
| pending_email | character varying | yes |
| password_hash | text |  |
| pin_hash | text | yes |
| status | integer |  |
| status_reason | text | yes |
| failed_login_count | integer |  |
| failed_pin_count | integer |  |
| lockout_end_utc | timestamp with time zone | yes |
| last_login_at | timestamp with time zone | yes |
| two_factor_enabled | boolean |  |
| must_change_password | boolean |  |
| security_stamp | text |  |
| password_changed_at | timestamp with time zone | yes |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |

Indexes: `UNIQUE ix_users_email USING btree (email) WHERE (email IS NOT NULL)`; `UNIQUE ix_users_mobile_number USING btree (mobile_number)`; `UNIQUE ix_users_public_id USING btree (public_id)`; `ix_users_status USING btree (status)`; `UNIQUE pk_users USING btree (id)`

### `user_profiles`

| Column | Type | Null |
|---|---|---|
| user_id | uuid |  |
| avatar_file_id | uuid | yes |
| address | text | yes |
| date_of_birth | date | yes |
| city | text | yes |
| state | text | yes |
| country | text | yes |
| time_zone | text |  |
| language | text |  |
| theme | integer |  |
| dashboard_layout_json | text | yes |
| updated_at | timestamp with time zone |  |

Indexes: `UNIQUE pk_user_profiles USING btree (user_id)`

### `user_security_settings`

| Column | Type | Null |
|---|---|---|
| user_id | uuid |  |
| login_alerts_enabled | boolean |  |
| vault_auto_lock_minutes | integer |  |
| pin_changed_at | timestamp with time zone | yes |
| two_factor_enabled_at | timestamp with time zone | yes |
| updated_at | timestamp with time zone |  |

Indexes: `UNIQUE pk_user_security_settings USING btree (user_id)`

### `user_authenticator_secrets`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid |  |
| encrypted_secret | text |  |
| is_confirmed | boolean |  |
| created_at | timestamp with time zone |  |
| confirmed_at | timestamp with time zone | yes |
| last_used_time_step | bigint |  |

Indexes: `ix_user_authenticator_secrets_user_id USING btree (user_id)`; `UNIQUE pk_user_authenticator_secrets USING btree (id)`

### `user_recovery_codes`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid |  |
| code_hash | text |  |
| created_at | timestamp with time zone |  |
| used_at | timestamp with time zone | yes |

Indexes: `ix_user_recovery_codes_user_id USING btree (user_id)`; `UNIQUE pk_user_recovery_codes USING btree (id)`

### `user_sessions`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid |  |
| device_id | uuid | yes |
| created_at | timestamp with time zone |  |
| last_activity_at | timestamp with time zone |  |
| absolute_expires_at | timestamp with time zone |  |
| revoked_at | timestamp with time zone | yes |
| revoked_reason | text | yes |
| reauthenticated_at | timestamp with time zone | yes |
| ip_address | text | yes |
| user_agent | text | yes |
| browser | text | yes |
| operating_system | text | yes |
| device_name | text | yes |
| impersonated_by_user_id | uuid | yes |
| parent_session_id | uuid | yes |

Indexes: `ix_user_sessions_user_id_revoked_at USING btree (user_id, revoked_at)`; `UNIQUE pk_user_sessions USING btree (id)`

### `refresh_tokens`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| session_id | uuid |  |
| token_hash | text |  |
| created_at | timestamp with time zone |  |
| expires_at | timestamp with time zone |  |
| revoked_at | timestamp with time zone | yes |
| replaced_by_token_id | uuid | yes |

Indexes: `ix_refresh_tokens_session_id USING btree (session_id)`; `UNIQUE ix_refresh_tokens_token_hash USING btree (token_hash)`; `UNIQUE pk_refresh_tokens USING btree (id)`

### `user_devices`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid |  |
| device_key | text |  |
| name | text | yes |
| browser | text | yes |
| operating_system | text | yes |
| device_type | text | yes |
| first_seen_at | timestamp with time zone |  |
| last_seen_at | timestamp with time zone |  |
| last_ip_address | text | yes |

Indexes: `UNIQUE ix_user_devices_user_id_device_key USING btree (user_id, device_key)`; `UNIQUE pk_user_devices USING btree (id)`

### `otp_codes`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid | yes |
| purpose | integer |  |
| target | text |  |
| code_hash | text |  |
| created_at | timestamp with time zone |  |
| expires_at | timestamp with time zone |  |
| attempts | integer |  |
| consumed_at | timestamp with time zone | yes |

Indexes: `ix_otp_codes_target_purpose_created_at USING btree (target, purpose, created_at)`; `UNIQUE pk_otp_codes USING btree (id)`

### `login_audit_logs`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid | yes |
| masked_identifier | text | yes |
| event | text |  |
| success | boolean |  |
| failure_reason | text | yes |
| ip_address | text | yes |
| user_agent | text | yes |
| browser | text | yes |
| operating_system | text | yes |
| device | text | yes |
| session_id | uuid | yes |
| timestamp | timestamp with time zone |  |

Indexes: `ix_login_audit_logs_timestamp USING btree ("timestamp")`; `ix_login_audit_logs_user_id_timestamp USING btree (user_id, "timestamp")`; `UNIQUE pk_login_audit_logs USING btree (id)`

### `audit_logs`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid | yes |
| event | text |  |
| entity_type | text | yes |
| entity_id | text | yes |
| details | text | yes |
| success | boolean |  |
| ip_address | text | yes |
| user_agent | text | yes |
| browser | text | yes |
| operating_system | text | yes |
| device | text | yes |
| session_id | uuid | yes |
| impersonated_by | uuid | yes |
| timestamp | timestamp with time zone |  |

Indexes: `ix_audit_logs_timestamp USING btree ("timestamp")`; `ix_audit_logs_user_id_timestamp USING btree (user_id, "timestamp")`; `UNIQUE pk_audit_logs USING btree (id)`

### `account_deletion_requests`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid |  |
| reason | text | yes |
| requested_at | timestamp with time zone |  |
| scheduled_for | timestamp with time zone |  |
| status | integer |  |
| completed_at | timestamp with time zone | yes |

Indexes: `ix_account_deletion_requests_status_scheduled_for USING btree (status, scheduled_for)`; `UNIQUE pk_account_deletion_requests USING btree (id)`

### `recent_searches`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid |  |
| query | text |  |
| created_at | timestamp with time zone |  |

Indexes: `ix_recent_searches_user_id_created_at USING btree (user_id, created_at)`; `UNIQUE pk_recent_searches USING btree (id)`

### `stored_files`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid |  |
| original_name | text |  |
| content_type | text |  |
| extension | text |  |
| size_bytes | bigint |  |
| storage_key | text |  |
| sha256 | text |  |
| purpose | text |  |
| created_at | timestamp with time zone |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `UNIQUE ix_stored_files_storage_key USING btree (storage_key)`; `ix_stored_files_user_id USING btree (user_id)`; `UNIQUE pk_stored_files USING btree (id)`

## Administration

### `roles`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| name | text |  |
| description | text | yes |
| is_system | boolean |  |

Indexes: `UNIQUE ix_roles_name USING btree (name)`; `UNIQUE pk_roles USING btree (id)`

### `permissions`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| key | text |  |
| description | text | yes |

Indexes: `UNIQUE ix_permissions_key USING btree (key)`; `UNIQUE pk_permissions USING btree (id)`

### `user_roles`

| Column | Type | Null |
|---|---|---|
| user_id | uuid |  |
| role_id | uuid |  |
| assigned_at | timestamp with time zone |  |

Indexes: `ix_user_roles_role_id USING btree (role_id)`; `UNIQUE pk_user_roles USING btree (user_id, role_id)`

### `role_permissions`

| Column | Type | Null |
|---|---|---|
| role_id | uuid |  |
| permission_id | uuid |  |

Indexes: `ix_role_permissions_permission_id USING btree (permission_id)`; `UNIQUE pk_role_permissions USING btree (role_id, permission_id)`

### `system_settings`

| Column | Type | Null |
|---|---|---|
| key | text |  |
| value | text |  |
| is_secret | boolean |  |
| updated_at | timestamp with time zone |  |
| updated_by | uuid | yes |

Indexes: `UNIQUE pk_system_settings USING btree (key)`

### `admin_audit_logs`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| admin_user_id | uuid |  |
| action | text |  |
| target_user_id | uuid | yes |
| details | text | yes |
| ip_address | text | yes |
| user_agent | text | yes |
| timestamp | timestamp with time zone |  |

Indexes: `ix_admin_audit_logs_timestamp USING btree ("timestamp")`; `UNIQUE pk_admin_audit_logs USING btree (id)`

### `user_reports`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| reporter_id | uuid |  |
| reported_user_id | uuid |  |
| reason | text |  |
| details | text | yes |
| status | integer |  |
| created_at | timestamp with time zone |  |
| reviewed_at | timestamp with time zone | yes |
| reviewed_by | uuid | yes |

Indexes: `ix_user_reports_status_created_at USING btree (status, created_at)`; `UNIQUE pk_user_reports USING btree (id)`

## Finance

### `expense_categories`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| name | text |  |
| type | integer |  |
| icon | text | yes |
| color | text | yes |
| is_default | boolean |  |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_expense_categories_user_id_type_name USING btree (user_id, type, name)`; `UNIQUE pk_expense_categories USING btree (id)`

### `expenses`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| name | text |  |
| amount | numeric |  |
| category_id | uuid |  |
| payment_method | integer |  |
| date | date |  |
| description | text | yes |
| notes | text | yes |
| receipt_file_id | uuid | yes |
| recurring_occurrence_id | uuid | yes |
| contact_id | uuid | yes |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_expenses_category_id USING btree (category_id)`; `ix_expenses_name USING gin (name gin_trgm_ops)`; `ix_expenses_user_id_date USING btree (user_id, date)`; `UNIQUE pk_expenses USING btree (id)`

### `income_records`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| title | text |  |
| source | text |  |
| amount | numeric |  |
| date | date |  |
| description | text | yes |
| notes | text | yes |
| contact_id | uuid | yes |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_income_records_title USING gin (title gin_trgm_ops)`; `ix_income_records_user_id_date USING btree (user_id, date)`; `UNIQUE pk_income_records USING btree (id)`

### `monthly_budgets`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| year | integer |  |
| month | integer |  |
| total_amount | numeric |  |
| alert_threshold_percent | integer |  |
| last_alert_sent_at | timestamp with time zone | yes |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `UNIQUE ix_monthly_budgets_user_id_year_month USING btree (user_id, year, month) WHERE (is_deleted = false)`; `UNIQUE pk_monthly_budgets USING btree (id)`

### `budget_categories`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| monthly_budget_id | uuid |  |
| category_id | uuid |  |
| amount | numeric |  |

Indexes: `ix_budget_categories_category_id USING btree (category_id)`; `ix_budget_categories_monthly_budget_id USING btree (monthly_budget_id)`; `UNIQUE pk_budget_categories USING btree (id)`

### `recurring_expenses`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| name | text |  |
| amount | numeric |  |
| category_id | uuid |  |
| payment_method | integer |  |
| frequency | integer |  |
| custom_interval_days | integer | yes |
| start_date | date |  |
| end_date | date | yes |
| reminder_days_before | integer |  |
| is_active | boolean |  |
| notes | text | yes |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_recurring_expenses_category_id USING btree (category_id)`; `ix_recurring_expenses_user_id USING btree (user_id)`; `UNIQUE pk_recurring_expenses USING btree (id)`

### `recurring_expense_occurrences`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| recurring_expense_id | uuid |  |
| user_id | uuid |  |
| due_date | date |  |
| amount | numeric |  |
| status | integer |  |
| paid_at | timestamp with time zone | yes |
| expense_id | uuid | yes |

Indexes: `UNIQUE ix_recurring_expense_occurrences_recurring_expense_id_due_date USING btree (recurring_expense_id, due_date)`; `ix_recurring_expense_occurrences_user_id_status_due_date USING btree (user_id, status, due_date)`; `UNIQUE pk_recurring_expense_occurrences USING btree (id)`

## Document vault

### `document_folders`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| name | text |  |
| parent_id | uuid | yes |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_document_folders_user_id_parent_id USING btree (user_id, parent_id)`; `UNIQUE pk_document_folders USING btree (id)`

### `documents`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| name | text |  |
| folder_id | uuid | yes |
| category | text |  |
| description | text | yes |
| current_version_id | uuid | yes |
| version_count | integer |  |
| size_bytes | bigint |  |
| content_type | text |  |
| extension | text |  |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_documents_folder_id USING btree (folder_id)`; `ix_documents_name USING gin (name gin_trgm_ops)`; `ix_documents_user_id_folder_id USING btree (user_id, folder_id)`; `UNIQUE pk_documents USING btree (id)`

### `document_versions`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| document_id | uuid |  |
| version_number | integer |  |
| stored_file_id | uuid |  |
| note | text | yes |
| uploaded_at | timestamp with time zone |  |

Indexes: `ix_document_versions_document_id USING btree (document_id)`; `ix_document_versions_stored_file_id USING btree (stored_file_id)`; `UNIQUE pk_document_versions USING btree (id)`

### `document_tags`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| document_id | uuid |  |
| tag | text |  |

Indexes: `UNIQUE ix_document_tags_document_id_tag USING btree (document_id, tag)`; `UNIQUE pk_document_tags USING btree (id)`

### `document_share_links`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid |  |
| document_id | uuid |  |
| token_hash | text |  |
| created_at | timestamp with time zone |  |
| expires_at | timestamp with time zone |  |
| max_downloads | integer | yes |
| download_count | integer |  |
| revoked_at | timestamp with time zone | yes |

Indexes: `ix_document_share_links_document_id USING btree (document_id)`; `UNIQUE ix_document_share_links_token_hash USING btree (token_hash)`; `UNIQUE pk_document_share_links USING btree (id)`

## Password vault

### `password_vaults`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid |  |
| kdf_algorithm | text |  |
| kdf_iterations | integer |  |
| kdf_salt | text |  |
| wrapped_key | text |  |
| recovery_salt | text |  |
| recovery_wrapped_key | text |  |
| format_version | integer |  |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |

Indexes: `UNIQUE ix_password_vaults_user_id USING btree (user_id)`; `UNIQUE pk_password_vaults USING btree (id)`

### `password_entries`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| title | text |  |
| website_url | text | yes |
| application_name | text | yes |
| category | text |  |
| tags | text | yes |
| is_favorite | boolean |  |
| encrypted_data | text |  |
| revision | integer |  |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_password_entries_user_id_category USING btree (user_id, category)`; `UNIQUE pk_password_entries USING btree (id)`

### `password_history`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid |  |
| password_entry_id | uuid |  |
| encrypted_password | text |  |
| changed_at | timestamp with time zone |  |

Indexes: `ix_password_history_password_entry_id_changed_at USING btree (password_entry_id, changed_at)`; `UNIQUE pk_password_history USING btree (id)`

### `secure_notes`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| encrypted_data | text |  |
| is_favorite | boolean |  |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_secure_notes_user_id USING btree (user_id)`; `UNIQUE pk_secure_notes USING btree (id)`

## Custom tables

### `custom_tables`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| name | text |  |
| description | text | yes |
| icon | text | yes |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_custom_tables_user_id USING btree (user_id)`; `UNIQUE pk_custom_tables USING btree (id)`

### `custom_table_columns`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| table_id | uuid |  |
| name | text |  |
| field_type | integer |  |
| sort_order | integer |  |
| is_required | boolean |  |
| is_unique | boolean |  |
| is_visible | boolean |  |
| default_value | text | yes |
| placeholder | text | yes |
| options_json | text | yes |
| min_value | numeric | yes |
| max_value | numeric | yes |
| max_length | integer | yes |
| regex_pattern | text | yes |
| formula | text | yes |
| is_deleted | boolean |  |

Indexes: `ix_custom_table_columns_table_id_sort_order USING btree (table_id, sort_order)`; `UNIQUE pk_custom_table_columns USING btree (id)`

### `custom_table_records`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| table_id | uuid |  |
| notes | text | yes |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_custom_table_records_table_id_created_at USING btree (table_id, created_at)`; `UNIQUE pk_custom_table_records USING btree (id)`

### `custom_table_values`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| record_id | uuid |  |
| column_id | uuid |  |
| text_value | text | yes |
| number_value | numeric | yes |
| date_value | timestamp with time zone | yes |
| bool_value | boolean | yes |
| file_id | uuid | yes |

Indexes: `ix_custom_table_values_column_id_date_value USING btree (column_id, date_value)`; `ix_custom_table_values_column_id_number_value USING btree (column_id, number_value)`; `ix_custom_table_values_column_id_text_value USING btree (column_id, text_value)`; `UNIQUE ix_custom_table_values_record_id_column_id USING btree (record_id, column_id)`; `UNIQUE pk_custom_table_values USING btree (id)`

### `custom_table_attachments`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| record_id | uuid |  |
| stored_file_id | uuid |  |
| created_at | timestamp with time zone |  |

Indexes: `ix_custom_table_attachments_record_id USING btree (record_id)`; `ix_custom_table_attachments_stored_file_id USING btree (stored_file_id)`; `UNIQUE pk_custom_table_attachments USING btree (id)`

## Products

### `product_registers`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| name | text |  |
| description | text | yes |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_product_registers_user_id USING btree (user_id)`; `UNIQUE pk_product_registers USING btree (id)`

### `product_custom_fields`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| register_id | uuid |  |
| name | text |  |
| field_type | integer |  |
| sort_order | integer |  |

Indexes: `ix_product_custom_fields_register_id USING btree (register_id)`; `UNIQUE pk_product_custom_fields USING btree (id)`

### `products`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| register_id | uuid |  |
| product_name | text |  |
| brand_name | text | yes |
| composition | text | yes |
| packing | text | yes |
| mrp | numeric | yes |
| purchase_price | numeric | yes |
| gst_percent | numeric | yes |
| hsn_code | text | yes |
| product_code | text | yes |
| manufacturer | text | yes |
| expiry_date | date | yes |
| image_file_id | uuid | yes |
| description | text | yes |
| custom_values | jsonb |  |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_products_product_name USING gin (product_name gin_trgm_ops)`; `ix_products_register_id_product_name USING btree (register_id, product_name)`; `UNIQUE pk_products USING btree (id)`

### `product_attachments`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| product_id | uuid |  |
| stored_file_id | uuid |  |
| created_at | timestamp with time zone |  |

Indexes: `ix_product_attachments_product_id USING btree (product_id)`; `ix_product_attachments_stored_file_id USING btree (stored_file_id)`; `UNIQUE pk_product_attachments USING btree (id)`

## Contacts & Udhar

### `contact_groups`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| name | text |  |
| color | text | yes |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_contact_groups_user_id USING btree (user_id)`; `UNIQUE pk_contact_groups USING btree (id)`

### `contacts`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| name | text |  |
| mobile | text | yes |
| alternate_mobile | text | yes |
| email | text | yes |
| address | text | yes |
| company | text | yes |
| category | text | yes |
| group_id | uuid | yes |
| notes | text | yes |
| avatar_file_id | uuid | yes |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_contacts_group_id USING btree (group_id)`; `ix_contacts_name USING gin (name gin_trgm_ops)`; `ix_contacts_user_id_name USING btree (user_id, name)`; `UNIQUE pk_contacts USING btree (id)`

### `contact_attachments`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| contact_id | uuid |  |
| stored_file_id | uuid |  |
| created_at | timestamp with time zone |  |

Indexes: `ix_contact_attachments_contact_id USING btree (contact_id)`; `ix_contact_attachments_stored_file_id USING btree (stored_file_id)`; `UNIQUE pk_contact_attachments USING btree (id)`

### `credit_transactions`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| direction | integer |  |
| contact_id | uuid | yes |
| person_name | text |  |
| contact_number | text | yes |
| amount | numeric |  |
| settled_amount | numeric |  |
| transaction_date | date |  |
| expected_return_date | date | yes |
| next_reminder_at | timestamp with time zone | yes |
| description | text | yes |
| status | integer |  |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_credit_transactions_contact_id USING btree (contact_id)`; `ix_credit_transactions_person_name USING gin (person_name gin_trgm_ops)`; `ix_credit_transactions_user_id_direction_status USING btree (user_id, direction, status)`; `UNIQUE pk_credit_transactions USING btree (id)`

### `credit_payments`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| transaction_id | uuid |  |
| amount | numeric |  |
| payment_date | date |  |
| method | integer |  |
| note | text | yes |
| created_at | timestamp with time zone |  |

Indexes: `ix_credit_payments_transaction_id USING btree (transaction_id)`; `UNIQUE pk_credit_payments USING btree (id)`

### `credit_reminders`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| transaction_id | uuid |  |
| user_id | uuid |  |
| remind_at | timestamp with time zone |  |
| note | text | yes |
| sent_at | timestamp with time zone | yes |

Indexes: `ix_credit_reminders_transaction_id_remind_at USING btree (transaction_id, remind_at)`; `UNIQUE pk_credit_reminders USING btree (id)`

### `credit_attachments`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| transaction_id | uuid |  |
| stored_file_id | uuid |  |
| created_at | timestamp with time zone |  |

Indexes: `ix_credit_attachments_stored_file_id USING btree (stored_file_id)`; `ix_credit_attachments_transaction_id USING btree (transaction_id)`; `UNIQUE pk_credit_attachments USING btree (id)`

## Diary & calendar

### `diary_entries`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| title | text |  |
| encrypted_content | text |  |
| entry_date | date |  |
| entry_time | time without time zone |  |
| mood | text | yes |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_diary_entries_user_id_entry_date USING btree (user_id, entry_date)`; `UNIQUE pk_diary_entries USING btree (id)`

### `diary_tags`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| entry_id | uuid |  |
| tag | text |  |

Indexes: `UNIQUE ix_diary_tags_entry_id_tag USING btree (entry_id, tag)`; `UNIQUE pk_diary_tags USING btree (id)`

### `diary_attachments`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| entry_id | uuid |  |
| stored_file_id | uuid |  |
| is_photo | boolean |  |
| created_at | timestamp with time zone |  |

Indexes: `ix_diary_attachments_entry_id USING btree (entry_id)`; `ix_diary_attachments_stored_file_id USING btree (stored_file_id)`; `UNIQUE pk_diary_attachments USING btree (id)`

### `calendar_events`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| title | text |  |
| description | text | yes |
| event_type | integer |  |
| start_utc | timestamp with time zone |  |
| end_utc | timestamp with time zone |  |
| all_day | boolean |  |
| location | text | yes |
| priority | integer |  |
| reminder_offsets | ARRAY |  |
| recurrence | integer |  |
| recurrence_until_utc | timestamp with time zone | yes |
| is_completed | boolean |  |
| contact_id | uuid | yes |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_calendar_events_title USING gin (title gin_trgm_ops)`; `ix_calendar_events_user_id_start_utc USING btree (user_id, start_utc)`; `UNIQUE pk_calendar_events USING btree (id)`

### `calendar_event_attachments`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| event_id | uuid |  |
| stored_file_id | uuid |  |
| created_at | timestamp with time zone |  |

Indexes: `ix_calendar_event_attachments_event_id USING btree (event_id)`; `ix_calendar_event_attachments_stored_file_id USING btree (stored_file_id)`; `UNIQUE pk_calendar_event_attachments USING btree (id)`

### `reminders`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| title | text |  |
| notes | text | yes |
| category | text |  |
| remind_at_utc | timestamp with time zone |  |
| repeat | integer |  |
| is_completed | boolean |  |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |
| user_id | uuid |  |
| is_deleted | boolean |  |
| deleted_at | timestamp with time zone | yes |

Indexes: `ix_reminders_user_id_remind_at_utc USING btree (user_id, remind_at_utc)`; `UNIQUE pk_reminders USING btree (id)`

### `reminder_schedules`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid |  |
| source_type | integer |  |
| source_id | uuid |  |
| dedupe_key | text |  |
| fire_at_utc | timestamp with time zone |  |
| occurrence_utc | timestamp with time zone | yes |
| status | integer |  |
| created_at | timestamp with time zone |  |
| processed_at | timestamp with time zone | yes |

Indexes: `UNIQUE ix_reminder_schedules_dedupe_key USING btree (dedupe_key)`; `ix_reminder_schedules_source_type_source_id USING btree (source_type, source_id)`; `ix_reminder_schedules_status_fire_at_utc USING btree (status, fire_at_utc)`; `UNIQUE pk_reminder_schedules USING btree (id)`

## Notifications & communication

### `notifications`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid |  |
| event_type | text |  |
| title | text |  |
| body | text | yes |
| link | text | yes |
| is_read | boolean |  |
| created_at | timestamp with time zone |  |
| read_at | timestamp with time zone | yes |

Indexes: `ix_notifications_user_id_is_read_created_at USING btree (user_id, is_read, created_at)`; `UNIQUE pk_notifications USING btree (id)`

### `notification_preferences`

| Column | Type | Null |
|---|---|---|
| user_id | uuid |  |
| email_enabled | boolean |  |
| sms_enabled | boolean |  |
| push_enabled | boolean |  |
| meeting_reminders | boolean |  |
| expense_reminders | boolean |  |
| udhar_reminders | boolean |  |
| general_reminders | boolean |  |
| friend_requests | boolean |  |
| chat_messages | boolean |  |
| login_alerts | boolean |  |
| marketing | boolean |  |
| updated_at | timestamp with time zone |  |

Indexes: `UNIQUE pk_notification_preferences USING btree (user_id)`

### `notification_policies`

| Column | Type | Null |
|---|---|---|
| event_type | text |  |
| display_name | text |  |
| in_app_enabled | boolean |  |
| email_enabled | boolean |  |
| sms_enabled | boolean |  |
| push_enabled | boolean |  |
| whats_app_enabled | boolean |  |
| is_security_critical | boolean |  |
| user_can_opt_out | boolean |  |
| updated_at | timestamp with time zone |  |
| updated_by | uuid | yes |

Indexes: `UNIQUE pk_notification_policies USING btree (event_type)`

### `notification_templates`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| event_type | text |  |
| channel | integer |  |
| subject | text | yes |
| body | text |  |
| provider_template_id | text | yes |
| is_active | boolean |  |
| updated_at | timestamp with time zone |  |
| updated_by | uuid | yes |

Indexes: `UNIQUE ix_notification_templates_event_type_channel USING btree (event_type, channel)`; `UNIQUE pk_notification_templates USING btree (id)`

### `notification_queue`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid | yes |
| event_type | text |  |
| channel | integer |  |
| encrypted_recipient | text |  |
| masked_recipient | text |  |
| subject | text | yes |
| encrypted_body | text | yes |
| encrypted_variables | text | yes |
| dedupe_key | text |  |
| status | integer |  |
| attempts | integer |  |
| max_attempts | integer |  |
| next_attempt_at | timestamp with time zone |  |
| last_error | text | yes |
| provider_id | uuid | yes |
| created_at | timestamp with time zone |  |
| sent_at | timestamp with time zone | yes |

Indexes: `UNIQUE ix_notification_queue_dedupe_key USING btree (dedupe_key)`; `ix_notification_queue_status_next_attempt_at USING btree (status, next_attempt_at)`; `UNIQUE pk_notification_queue USING btree (id)`

### `push_subscriptions`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid |  |
| endpoint | text |  |
| p256dh | text |  |
| auth | text |  |
| user_agent | text | yes |
| created_at | timestamp with time zone |  |
| last_success_at | timestamp with time zone | yes |

Indexes: `UNIQUE ix_push_subscriptions_endpoint USING btree (endpoint)`; `UNIQUE pk_push_subscriptions USING btree (id)`

### `communication_providers`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| channel | integer |  |
| provider_type | integer |  |
| name | text |  |
| is_active | boolean |  |
| is_default | boolean |  |
| priority | integer |  |
| settings_json | text |  |
| encrypted_secrets_json | text | yes |
| last_tested_at | timestamp with time zone | yes |
| last_test_succeeded | boolean | yes |
| last_test_message | text | yes |
| created_at | timestamp with time zone |  |
| updated_at | timestamp with time zone |  |
| created_by | uuid | yes |
| updated_by | uuid | yes |

Indexes: `ix_communication_providers_channel_is_active_priority USING btree (channel, is_active, priority)`; `UNIQUE pk_communication_providers USING btree (id)`

### `communication_logs`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid | yes |
| queue_item_id | uuid | yes |
| channel | integer |  |
| event_type | text |  |
| masked_recipient | text |  |
| provider_name | text | yes |
| status | integer |  |
| failure_reason | text | yes |
| attempt | integer |  |
| will_retry | boolean |  |
| created_at | timestamp with time zone |  |

Indexes: `ix_communication_logs_channel_status USING btree (channel, status)`; `ix_communication_logs_created_at USING btree (created_at)`; `UNIQUE pk_communication_logs USING btree (id)`

### `announcements`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| title | text |  |
| body | text |  |
| target | integer |  |
| target_value | text | yes |
| send_in_app | boolean |  |
| send_email | boolean |  |
| send_push | boolean |  |
| send_sms | boolean |  |
| recipient_count | integer |  |
| created_by | uuid |  |
| created_at | timestamp with time zone |  |

Indexes: `ix_announcements_created_at USING btree (created_at)`; `UNIQUE pk_announcements USING btree (id)`

## Friends & chat

### `friend_invitations`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| inviter_user_id | uuid |  |
| code | text |  |
| created_at | timestamp with time zone |  |
| expires_at | timestamp with time zone |  |
| max_uses | integer |  |
| used_count | integer |  |
| revoked_at | timestamp with time zone | yes |

Indexes: `UNIQUE ix_friend_invitations_code USING btree (code)`; `UNIQUE pk_friend_invitations USING btree (id)`

### `friend_requests`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| from_user_id | uuid |  |
| to_user_id | uuid |  |
| status | integer |  |
| message | text | yes |
| created_at | timestamp with time zone |  |
| responded_at | timestamp with time zone | yes |

Indexes: `ix_friend_requests_from_user_id_to_user_id_status USING btree (from_user_id, to_user_id, status)`; `ix_friend_requests_to_user_id_status USING btree (to_user_id, status)`; `UNIQUE pk_friend_requests USING btree (id)`

### `friend_connections`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_a_id | uuid |  |
| user_b_id | uuid |  |
| conversation_id | uuid |  |
| created_at | timestamp with time zone |  |

Indexes: `UNIQUE ix_friend_connections_user_a_id_user_b_id USING btree (user_a_id, user_b_id)`; `UNIQUE pk_friend_connections USING btree (id)`

### `chat_conversations`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| created_at | timestamp with time zone |  |
| last_message_at | timestamp with time zone | yes |
| disappearing_seconds | integer | yes |

Indexes: `UNIQUE pk_chat_conversations USING btree (id)`

### `chat_participants`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| conversation_id | uuid |  |
| user_id | uuid |  |
| joined_at | timestamp with time zone |  |
| last_read_at | timestamp with time zone | yes |
| last_delivered_at | timestamp with time zone | yes |

Indexes: `UNIQUE ix_chat_participants_conversation_id_user_id USING btree (conversation_id, user_id)`; `ix_chat_participants_user_id USING btree (user_id)`; `UNIQUE pk_chat_participants USING btree (id)`

### `chat_messages`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| conversation_id | uuid |  |
| sender_id | uuid |  |
| type | integer |  |
| body | text | yes |
| reply_to_message_id | uuid | yes |
| created_at | timestamp with time zone |  |
| deleted_at | timestamp with time zone | yes |
| expires_at | timestamp with time zone | yes |

Indexes: `ix_chat_messages_conversation_id_created_at USING btree (conversation_id, created_at)`; `ix_chat_messages_expires_at USING btree (expires_at) WHERE (expires_at IS NOT NULL)`; `UNIQUE pk_chat_messages USING btree (id)`

### `chat_attachments`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| message_id | uuid |  |
| stored_file_id | uuid |  |
| file_name | text |  |
| content_type | text |  |
| size_bytes | bigint |  |

Indexes: `ix_chat_attachments_message_id USING btree (message_id)`; `ix_chat_attachments_stored_file_id USING btree (stored_file_id)`; `UNIQUE pk_chat_attachments USING btree (id)`

### `user_presences`

| Column | Type | Null |
|---|---|---|
| user_id | uuid |  |
| is_online | boolean |  |
| connection_count | integer |  |
| last_seen_at | timestamp with time zone |  |
| last_heartbeat_at | timestamp with time zone |  |

Indexes: `UNIQUE pk_user_presences USING btree (user_id)`

### `blocked_users`

| Column | Type | Null |
|---|---|---|
| id | uuid |  |
| user_id | uuid |  |
| blocked_user_id | uuid |  |
| created_at | timestamp with time zone |  |

Indexes: `UNIQUE ix_blocked_users_user_id_blocked_user_id USING btree (user_id, blocked_user_id)`; `UNIQUE pk_blocked_users USING btree (id)`
