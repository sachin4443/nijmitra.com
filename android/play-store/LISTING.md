# Google Play — store listing & app-content answers

## Graphics — what to upload where (Play Console → Grow users → Store presence → Main store listing)

| Play Console field | File(s) | Size | Required? |
|---|---|---|---|
| App icon | `icon-512.png` | 512 × 512 PNG | **Yes** |
| Feature graphic | `feature-graphic-1024x500.png` | 1024 × 500 | **Yes** |
| Phone screenshots (English listing) | `screenshots-marketing-en/01…08` (with headings) — or the plain `screenshots/` | 1080 × 1920 (9:16) | **Yes** (2–8) |
| Phone screenshots (Hindi translation `hi-IN`) | `screenshots-marketing-hi/01…08` | 1080 × 1920 | Optional |
| 7-inch tablet screenshots | `tablet-7in/01…06` | 1080 × 1920 (9:16) | Recommended |
| 10-inch tablet screenshots | `tablet-10in/01…06` | 2560 × 1440 (16:9) | Recommended |
| Video (YouTube link) | — | — | Optional |

Play requires phone/tablet screenshots in 16:9 or 9:16 — every file here matches. Foldable screenshots are optional and not included.
All screenshots show demo data (made-up names and amounts), not real users.

## Store listing (English)

**App name** (≤30): `NijMitra – Personal Life Manager`

**Short description** (≤80):
`Expenses, documents, passwords, Udhar, diary & reminders — private, in one app.`

**Full description** (≤4000):

```
NijMitra is your personal digital companion — one private, secure app for the everyday records of your life.

💰 EXPENSES, INCOME & BUDGET
• Add expenses and income in seconds with categories and payment methods (Cash, UPI, cards…)
• Monthly budgets with category limits and overspending alerts
• Recurring bills — rent, EMI, insurance, subscriptions — with due-date reminders
• Daily, monthly and yearly reports; export to PDF or Excel

📁 DOCUMENT VAULT
• Keep Aadhaar, PAN, certificates, insurance, property papers and bills in folders
• Open PDFs, photos, Excel and Word files right inside the app — download only when you need to
• Versions, tags and secure share links that expire automatically

🔐 PASSWORD MANAGER
• Save logins and secure notes, encrypted on your phone with a master password only you know
• Strong password generator, password history, encrypted backup

🤝 UDHAR (CREDIT) MANAGER
• Track who owes you and whom you owe — partial payments, overdue alerts and reminders
• Person-wise statements in PDF/Excel

📇 CONTACTS • 📓 DAILY DIARY • 📅 CALENDAR & REMINDERS
• Contacts with groups, attachments and money history
• A private diary with moods, tags and photos
• Meetings, birthdays and bill payments with reminders — even when the app is closed

🧾 PRODUCT REGISTER & CUSTOM TABLES
• Maintain medicine/stock/asset registers with MRP, GST, HSN and expiry
• Build your own registers with 16 field types and formulas — no coding

💬 FRIENDS & PRIVATE CHAT
• Connect by NijMitra ID or invite code; share photos and files; read receipts; disappearing messages

🛡️ PRIVACY & SECURITY
• Optional two-step verification (Google/Microsoft Authenticator)
• Automatic sign-out when idle, device & session management, sign-in alerts
• No ads. We never sell your data. Download all your data or delete your account at any time.

Works the same on your phone, tablet and computer at nijmitra.com — your data stays in sync.
English and हिन्दी.
```

## Store listing (Hindi — add as translation `hi-IN`)

**App name:** `NijMitra – निजी डिजिटल साथी`

**Short description:** `खर्च, दस्तावेज़, पासवर्ड, उधार, डायरी और रिमाइंडर — सब एक सुरक्षित ऐप में।`

**Full description:**

```
NijMitra — आपका व्यक्तिगत डिजिटल साथी। अपनी पूरी निजी दुनिया अब एक ही App में।

💰 खर्च, आय और बजट — हर खर्च जोड़ें, मासिक बजट बनाएं, बिल रिमाइंडर और रिपोर्ट (PDF/Excel)।
📁 दस्तावेज़ वॉल्ट — आधार, पैन, सर्टिफ़िकेट, बिल; PDF, फ़ोटो, Excel और Word फ़ाइलें ऐप में ही खोलें।
🔐 पासवर्ड मैनेजर — आपके फ़ोन पर एन्क्रिप्टेड, मास्टर पासवर्ड सिर्फ़ आपके पास।
🤝 उधार प्रबंधन — किससे लेना है, किसे देना है; आंशिक भुगतान, ड्यू-डेट रिमाइंडर, स्टेटमेंट।
📇 संपर्क डायरी • 📓 डेली डायरी • 📅 कैलेंडर और रिमाइंडर
🧾 प्रोडक्ट रजिस्टर और कस्टम टेबल — अपना रजिस्टर खुद बनाएं।
💬 दोस्तों से निजी चैट — फ़ोटो, फ़ाइलें, रीड रिसीट।
🛡️ टू-स्टेप वेरिफ़िकेशन, ऑटो साइन-आउट, कोई विज्ञापन नहीं, डेटा कभी बेचा नहीं जाता। कभी भी पूरा डेटा डाउनलोड करें या अकाउंट हटाएं।
```

**Category:** Productivity · **Tags:** Personal finance, Productivity, Organizer
**Contact email:** the support address configured in Admin → Website (required by Play).
**Website:** https://nijmitra.com · **Privacy policy:** https://nijmitra.com/privacy.html

## Data safety form

Answers that match how NijMitra actually works (keep them in sync with `privacy.html`):

| Question | Answer |
|---|---|
| Does your app collect or share user data? | **Yes, collects.** Does **not** share (delivery providers acting on our behalf are "service providers", not sharing). |
| Is all user data encrypted in transit? | **Yes** (HTTPS only). |
| Can users request data deletion? | **Yes** — in app (Settings → Data & account) and at https://nijmitra.com/delete-account.html |
| Account creation | Users create an account with mobile number + password. |

Data types collected (all: **collected, not shared, required unless noted, purpose = App functionality + Account management; security items also Fraud prevention, security & compliance**; none processed ephemerally only):

| Category → type | Notes |
|---|---|
| Personal info → **Name** | required |
| Personal info → **Phone number** | required (sign-in) |
| Personal info → **Email address** | optional |
| Personal info → **Address**, **Other info** (date of birth) | optional profile fields |
| Financial info → **Other financial info** | expenses, income, budgets, Udhar entries the user records |
| Messages → **Other in-app messages** | friend chat |
| Photos and videos → **Photos**, **Videos** | only files the user chooses to upload |
| Files and docs → **Files and docs** | Document Vault and attachments |
| Calendar → **Calendar events** | events/reminders created in NijMitra (not the phone calendar) |
| Contacts → **Contacts** | contacts the user types into NijMitra (the phone address book is not read) |
| App activity → **In-app search history**, **Other user-generated content** | recent searches; diary, custom tables, product registers |
| Device or other IDs → **Device or other IDs** | push-notification token and session/device records for security |

Not collected: location, health, web browsing history, audio files (unless uploaded by the user as attachments — then covered by Files and docs), advertising ID, crash/diagnostic SDK data.

## Content rating notes
- Users can interact (friend-only chat, file sharing) → declare **user interaction / shares info**.
- No violence, gambling, sexual content, controlled substances. Not designed for children.
