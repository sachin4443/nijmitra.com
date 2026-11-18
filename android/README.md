# NijMitra Android app

The Android app is a **Trusted Web Activity (TWA)** built with Google's
[android-browser-helper](https://github.com/GoogleChrome/android-browser-helper). It opens
`https://nijmitra.com` full-screen (no browser bar) using the phone's Chrome, so every feature,
fix and security update on the website is in the app immediately — no new app release needed.

| | |
|---|---|
| Package name | `com.nijmitra.app` (permanent — cannot be changed after the first upload) |
| Opens | `https://nijmitra.com/login` (signed-in users go straight to their dashboard) |
| Version | 1.0.2 (versionCode 3) |
| Min / target SDK | Android 7.0 (24) / Android 16 (36) |
| Permissions | `INTERNET`, `POST_NOTIFICATIONS` (Android 13+, only when the user enables alerts) |
| Size | ~1.5 MB bundle |

What the app adds on top of the website:
- launcher icon (adaptive + themed/monochrome), splash screen, brand-coloured status/navigation bars;
- web-push reminders shown as **NijMitra app notifications** (notification delegation);
- `https://nijmitra.com/...` links open in the app (verified App Links);
- falls back to a Custom Tab if no browser on the phone supports TWAs.

## 1. Before the first release — Digital Asset Links

The app only hides the browser bar when the website proves it trusts the app:
`https://nijmitra.com/.well-known/assetlinks.json` must list the SHA-256 fingerprint of the key that
signs the installed app. This file is served by the NijMitra API and managed in
**Admin → Platform Settings → Website & footer → Android app**.

1. The upload-key fingerprint is already filled in.
2. After uploading the first bundle to Play Console, open **Test and release → App integrity → App signing**,
   copy the **App signing key certificate → SHA-256 fingerprint** and add it as a second line in the admin
   field. Save. (Google re-signs the app with this key, so without it the installed app shows a URL bar.)
3. Check: open `https://nijmitra.com/.well-known/assetlinks.json` — it should list both fingerprints.

## 2. Building

Requirements: JDK 17+ (21 tested), Android SDK with `platforms;android-36` and `build-tools;36.0.0`.

```bash
cd android
echo "sdk.dir=/path/to/Android/sdk" > local.properties
./gradlew bundleRelease      # → app/build/outputs/bundle/release/app-release.aab (upload to Play)
./gradlew assembleRelease    # → app/build/outputs/apk/release/app-release.apk (install directly for testing)
```

Signing reads `android/keystore.properties` (never committed — see `.gitignore`):

```properties
storeFile=/secure/path/nijmitra-upload.jks
storePassword=…
keyAlias=nijmitra-upload
keyPassword=…
```

or the environment variables `NIJMITRA_KEYSTORE`, `NIJMITRA_KEYSTORE_PASSWORD`, `NIJMITRA_KEY_ALIAS`,
`NIJMITRA_KEY_PASSWORD` (for CI).

**The upload keystore is not in Git.** Keep `nijmitra-upload.jks` and its password in two safe places.
With Play App Signing, a lost upload key can be reset through Play Console support, but releases are
blocked until then.

For every new release increase `versionCode` (and usually `versionName`) in `app/build.gradle`.
The website itself is updated by deploying the web app — the Android app does not need a new release for that.

## 3. Publishing checklist (Google Play Console)

1. Create the app: name **NijMitra**, default language English (India), App, Free.
2. **Store listing** — texts and the *what-to-upload-where* table are in [`play-store/LISTING.md`](play-store/LISTING.md); graphics in `play-store/`
   (icon, feature graphic, phone screenshots with headings in English and Hindi, plain phone screenshots, 7" and 10" tablet screenshots).
3. **App content**
   - Privacy policy: `https://nijmitra.com/privacy.html`
   - Data safety: answers in [`play-store/LISTING.md`](play-store/LISTING.md#data-safety-form)
   - Account deletion URL: `https://nijmitra.com/delete-account.html`
   - Ads: **No ads**. Target audience: 18+. Content rating questionnaire: Utility/Productivity, user-to-user chat = **Yes** (friends only, report & block available).
   - App access: provide a test account (mobile number + password) for the reviewer.
   - Financial features declaration: the app offers **personal finance tracking / budgeting only** — no loans, payments, banking, crypto or investment services.
   - Government apps: **No**. Health: **No**. News: **No**.
4. Upload `app-release.aab` to **Internal testing** first, install from the Play link on a phone, check that
   the app opens without a URL bar (if a bar shows, step 1.2 is missing), then promote to Production.
5. New personal developer accounts must run a **closed test with at least 12 testers for 14 days**
   before production access is granted.
