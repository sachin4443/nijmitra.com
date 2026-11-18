import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { Dropdown } from 'react-bootstrap'
import { FiCheck, FiGlobe } from 'react-icons/fi'
import { api } from './api'

/**
 * Public-website languages. English is the default; Hindi is chosen automatically only when the device's
 * first preferred language is Hindi (or a closely related language we don't translate separately).
 * A choice made in the language menu is remembered on this device and always wins.
 */
export type Lang = 'en' | 'hi'
export const LANGUAGES: { code: Lang; label: string; native: string }[] = [
  { code: 'en', label: 'English', native: 'English' },
  { code: 'hi', label: 'Hindi', native: 'हिन्दी' },
]
const STORAGE_KEY = 'nm-lang'

export function detectLanguage(): Lang {
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved === 'en' || saved === 'hi') return saved
  } catch { /* storage blocked */ }
  const first = (navigator.languages?.[0] ?? navigator.language ?? 'en').toLowerCase()
  return /^(hi|mr|mai|bho|awa|raj|mag)\b/.test(first) ? 'hi' : 'en'
}

const en = {
  'nav.features': 'Features', 'nav.security': 'Security', 'nav.how': 'How it works', 'nav.apps': 'Apps', 'nav.login': 'Login', 'nav.register': 'Register',
  'lang.label': 'Language',
  'hero.pill': 'Your Personal Digital Companion',
  'hero.title1': 'Your whole', 'hero.titleHl': 'private world', 'hero.title2': 'now in one', 'hero.titleApp': 'App',
  'hero.sub': 'Money, expenses, documents, passwords, Udhar, diary, meetings and chat with friends — all safe, private and organised.',
  'hero.sub2': 'Manage everything from one secure app on your mobile, tablet and computer.',
  'hero.cta': 'Join today — Register free', 'hero.b1': 'Safe & private', 'hero.b2': 'Free to start', 'hero.b3': 'English + हिन्दी',
  'hero.alt': 'NijMitra app — all features in one app',
  'features.eyebrow': 'What can you do?', 'features.title': 'Everything your personal life needs',
  'f.finance': 'Expenses, income & budget', 'f.finance.d': 'Add every expense, track income and set monthly budgets — with category-wise reports, recurring bills and overspending alerts.',
  'f.docs': 'Document vault', 'f.docs.d': 'Aadhaar, PAN, certificates, bills — folders, tags, versions, instant preview and secure expiring share links.',
  'f.pw': 'Password manager', 'f.pw.d': 'Keep all your IDs and passwords safe — encrypted on your own device with a master password only you know.',
  'f.contacts': 'Contact directory', 'f.contacts.d': 'Family, friends, customers, suppliers — groups, attachments and a complete money history for every person.',
  'f.udhar': 'Udhar / credit ledger', 'f.udhar.d': 'Who owes you and whom you owe — partial payments, due-date reminders and person-wise statements.',
  'f.cal': 'Calendar & meetings', 'f.cal.d': 'Meetings, birthdays, bill payments and follow-ups in day, week, month and agenda views.',
  'f.rem': 'Smart reminders', 'f.rem.d': 'Reminded on time — in-app, push, email or SMS, even when the app is closed.',
  'f.diary': 'Private daily diary', 'f.diary.d': 'Write your thoughts and everyday moments — photos, moods, tags, timeline and archives.',
  'f.tables': 'Custom tables', 'f.tables.d': 'Build your own register for anything — 16 field types, formulas, Excel import and export.',
  'f.products': 'Product register', 'f.products.d': 'Medicines, stock or assets — MRP, GST, HSN, expiry tracking, images and Excel/PDF export.',
  'f.chat': 'Friends & private chat', 'f.chat.d': 'Connect by NijMitra ID or invite code — real-time chat, files, read receipts and disappearing messages.',
  'f.notif': 'Notifications', 'f.notif.d': 'You decide what reaches you — choose email, SMS, push and in-app alerts for every category.',
  'show.eyebrow': 'One app, every feature', 'show.title': 'Stop juggling diaries, spreadsheets and ten different apps',
  'show.l1': 'Global search finds a contact, expense, document or saved login in seconds',
  'show.l2': 'Dashboard shows income, expenses, budget, money to receive and to pay',
  'show.l3': 'Excel, CSV and PDF export for reports, registers and Udhar statements',
  'show.l4': 'Light and dark themes, installable on any phone or computer',
  'show.l5': 'Your data is yours — export everything anytime', 'show.cta': 'Create my free account', 'show.alt': 'NijMitra dashboard',
  'sec.eyebrow': 'Safe & private', 'sec.title': 'Built to protect your personal life',
  's.vault': 'Zero-knowledge password vault', 's.vault.d': 'Passwords are encrypted in your browser (AES-256) with a key only you hold. Our servers store only unreadable ciphertext.',
  's.2fa': 'Two-step verification', 's.2fa.d': 'Protect sign-in with Google or Microsoft Authenticator plus one-time recovery codes.',
  's.idle': 'Automatic sign-out', 's.idle.d': 'Inactive sessions end automatically. See every device and sign out remotely in one tap.',
  's.private': 'Private by design', 's.private.d': 'Your diary and vault are never shown in admin screens. Every sensitive action is audit-logged.',
  's.enc': 'Encrypted & exportable', 's.enc.d': 'HTTPS everywhere, sensitive fields encrypted at rest, and one-click export of all your data.',
  's.only': 'Only you see your data', 's.only.d': 'Every record is tied to your account and checked on the server — no one else can open it.',
  'how.eyebrow': 'Easy to start', 'how.title': 'Get started in 3 simple steps',
  'how.1': 'Register', 'how.1.d': 'Create your account with your mobile number and a password, and set a personal security PIN.',
  'how.2': 'Secure', 'how.2.d': 'Turn on two-step verification in Settings and create your password vault.',
  'how.3': 'Organise', 'how.3.d': 'Start adding expenses, documents, contacts, Udhar and reminders — everything shows on your dashboard.',
  'apps.eyebrow': 'On mobile, tablet and computer', 'apps.title': 'Use NijMitra on every device',
  'apps.text': 'Get the Android app, or open nijmitra.com in any browser and choose “Install” / “Add to Home Screen” — it works like a regular app with its own icon and notifications.',
  'apps.web': 'Web App', 'apps.web.d': 'Any modern browser', 'apps.android': 'Android', 'apps.android.d': 'Chrome → Install app', 'apps.android.play': 'Get it on Google Play',
  'apps.ios': 'iPhone & iPad', 'apps.ios.d': 'Safari → Add to Home Screen', 'apps.desktop': 'Desktop', 'apps.desktop.d': 'Chrome / Edge → Install',
  'cta.title': 'Join NijMitra today', 'cta.text': 'Your personal digital companion — simple, secure and always with you.', 'cta.register': 'Register free',
  'footer.works': 'Works on mobile, tablet & desktop', 'footer.contact': 'Contact',
  'auth.title1': 'Your whole', 'auth.titleHl': 'private world', 'auth.title2': 'now in one App',
  'auth.sub': 'Your personal digital companion — your money, records and reminders, safely in one place.',
  'auth.f1': 'Money, budgets & Udhar', 'auth.f1.d': 'Track every rupee, bill and loan in one place.',
  'auth.f2': 'Zero-knowledge password vault', 'auth.f2.d': 'Encrypted on your device — not even we can read it.',
  'auth.f3': 'Strong security', 'auth.f3.d': 'Two-step verification, auto sign-out and full audit trail.',
  'auth.f4': 'Diary, documents & reminders', 'auth.f4.d': 'Your private life, organised and searchable.',
  'auth.f5': 'Private chat with friends', 'auth.f5.d': 'Connect by NijMitra ID or invite code.',
  'login.title': 'Welcome back', 'login.sub': 'Sign in with your mobile number and password.', 'login.mobile': 'Mobile number', 'login.password': 'Password',
  'login.forgot': 'Forgot password?', 'login.submit': 'Sign in', 'login.new': 'New to NijMitra?', 'login.create': 'Create your account',
  'login.mobileErr': 'Enter your 10-digit mobile number', 'login.passwordErr': 'Enter your password', 'login.showPw': 'Show password',
  'login.reason.idle': 'You were signed out after a period of inactivity.', 'login.reason.expired': 'Your session has ended. Please sign in again.',
  'login.reason.inactive': 'Your account is not active. Please contact support.', 'login.registered': 'Account created. Sign in to continue.',
  'otp.title': 'Two-step verification', 'otp.recoveryHelp': 'Enter one of your saved recovery codes.',
  'otp.help': 'Open Google Authenticator or Microsoft Authenticator and enter the 6-digit code for NijMitra.',
  'otp.submit': 'Verify & sign in', 'otp.useApp': 'Use authenticator code', 'otp.useRecovery': 'Use a recovery code', 'otp.back': 'Back',
  'setup.title': 'Set up two-step verification',
  'reg.title': 'Create your account', 'reg.sub': 'It takes less than a minute. You can enable two-step verification after signing in.',
  'reg.name': 'Full name', 'reg.mobile': 'Mobile number', 'reg.otp': 'Mobile OTP', 'reg.sendOtp': 'Send OTP', 'reg.resend': 'Resend',
  'reg.otpPh': 'Enter the 6-digit code', 'reg.otpPhFirst': 'Tap "Send OTP" first', 'reg.password': 'Password', 'reg.confirm': 'Confirm password',
  'reg.pin': 'Personal security PIN', 'reg.pinHelp': '(4–6 digits, used to confirm sensitive actions)',
  'reg.agree1': 'I agree to the', 'reg.terms': 'Terms of Use', 'reg.and': 'and', 'reg.privacy': 'Privacy Policy', 'reg.submit': 'Create account',
  'reg.have': 'Already have an account?', 'reg.signin': 'Sign in', 'reg.closed': 'New registrations are currently closed. Please check back later.', 'reg.back': 'Back to sign in',
  'reg.e.name': 'Enter your full name', 'reg.e.mobile': 'Enter a valid 10-digit Indian mobile number', 'reg.e.pwLen': 'At least 8 characters',
  'reg.e.pwMix': 'Use 3 of: uppercase, lowercase, number, symbol', 'reg.e.pin': 'PIN must be 4–6 digits', 'reg.e.terms': 'Please accept the terms to continue',
  'reg.e.match': 'Passwords do not match', 'reg.e.otp': 'Enter the code sent to your mobile',
  'fp.title': 'Reset password', 'fp.sub': "We'll send a verification code to your registered mobile number or email.", 'fp.mobile': 'Mobile number',
  'fp.send': 'Send code', 'fp.code': 'Verification code', 'fp.new': 'New password', 'fp.confirm': 'Confirm new password', 'fp.submit': 'Set new password', 'fp.back': 'Back to sign in',
}
export type TKey = keyof typeof en

const hi: Partial<Record<TKey, string>> = {
  'nav.features': 'सुविधाएं', 'nav.security': 'सुरक्षा', 'nav.how': 'कैसे काम करता है', 'nav.apps': 'ऐप्स', 'nav.login': 'लॉगिन', 'nav.register': 'रजिस्टर',
  'lang.label': 'भाषा',
  'hero.pill': 'आपका व्यक्तिगत डिजिटल साथी',
  'hero.title1': 'अपनी पूरी', 'hero.titleHl': 'निजी दुनिया', 'hero.title2': 'अब एक ही', 'hero.titleApp': 'App में',
  'hero.sub': 'पैसा, खर्च, दस्तावेज़, पासवर्ड, उधार, डायरी, मीटिंग और दोस्तों से चैट — सब कुछ सुरक्षित, निजी और व्यवस्थित।',
  'hero.sub2': 'मोबाइल, टैबलेट और कंप्यूटर पर एक ही सुरक्षित ऐप से सब कुछ संभालें।',
  'hero.cta': 'आज ही जुड़ें — मुफ़्त रजिस्टर करें', 'hero.b1': 'सुरक्षित और निजी', 'hero.b2': 'शुरुआत मुफ़्त', 'hero.b3': 'हिन्दी + English',
  'hero.alt': 'NijMitra ऐप — सारी सुविधाएं एक ऐप में',
  'features.eyebrow': 'क्या-क्या कर सकते हैं?', 'features.title': 'आपकी निजी ज़िंदगी की हर ज़रूरत',
  'f.finance': 'खर्च, आय और बजट', 'f.finance.d': 'हर खर्च जोड़ें, आय जानें, मासिक बजट बनाएं — कैटेगरी-वार रिपोर्ट, रिकरिंग बिल और ज़्यादा खर्च पर अलर्ट।',
  'f.docs': 'दस्तावेज़ वॉल्ट', 'f.docs.d': 'आधार, पैन, सर्टिफ़िकेट, बिल — फ़ोल्डर, टैग, वर्ज़न, तुरंत प्रीव्यू और सुरक्षित शेयर लिंक।',
  'f.pw': 'पासवर्ड मैनेजर', 'f.pw.d': 'अपनी सभी IDs और पासवर्ड सुरक्षित रखें — आपके अपने डिवाइस पर, आपके मास्टर पासवर्ड से एन्क्रिप्टेड।',
  'f.contacts': 'संपर्क डायरी', 'f.contacts.d': 'परिवार, मित्र, ग्राहक, सप्लायर — ग्रुप, अटैचमेंट और हर व्यक्ति का पूरा लेन-देन।',
  'f.udhar': 'उधार प्रबंधन', 'f.udhar.d': 'किससे लेना है, किसे देना है — आंशिक भुगतान, ड्यू-डेट रिमाइंडर और व्यक्ति-वार स्टेटमेंट।',
  'f.cal': 'कैलेंडर और मीटिंग', 'f.cal.d': 'मीटिंग, जन्मदिन, बिल भुगतान और फ़ॉलो-अप — दिन, सप्ताह, महीना और एजेंडा व्यू में।',
  'f.rem': 'स्मार्ट रिमाइंडर', 'f.rem.d': 'समय पर याद दिलाए — इन-ऐप, पुश, ईमेल या SMS, ऐप बंद होने पर भी।',
  'f.diary': 'डेली डायरी', 'f.diary.d': 'अपने विचार और रोज़ की बातें लिखें — फ़ोटो, मूड, टैग, टाइमलाइन और आर्काइव।',
  'f.tables': 'कस्टम टेबल', 'f.tables.d': 'अपनी ज़रूरत के अनुसार खुद का रजिस्टर बनाएं — 16 फ़ील्ड टाइप, फ़ॉर्मूला, Excel इम्पोर्ट और एक्सपोर्ट।',
  'f.products': 'प्रोडक्ट रजिस्टर', 'f.products.d': 'दवाइयां, स्टॉक या संपत्ति — MRP, GST, HSN, एक्सपायरी ट्रैकिंग, फ़ोटो और Excel/PDF एक्सपोर्ट।',
  'f.chat': 'दोस्तों से चैट', 'f.chat.d': 'NijMitra ID या इनवाइट कोड से जुड़ें — रियल-टाइम चैट, फ़ाइलें, रीड रिसीट और गायब होने वाले मैसेज।',
  'f.notif': 'सूचनाएं', 'f.notif.d': 'आप तय करें क्या आप तक पहुंचे — हर कैटेगरी के लिए ईमेल, SMS, पुश और इन-ऐप अलर्ट चुनें।',
  'show.eyebrow': 'एक App, सारी सुविधाएं', 'show.title': 'डायरी, स्प्रेडशीट और दस अलग-अलग ऐप्स की झंझट खत्म',
  'show.l1': 'ग्लोबल सर्च से कोई भी संपर्क, खर्च, दस्तावेज़ या लॉगिन सेकंडों में खोजें',
  'show.l2': 'डैशबोर्ड पर आय, खर्च, बजट, लेना और देना — सब एक नज़र में',
  'show.l3': 'रिपोर्ट, रजिस्टर और उधार स्टेटमेंट का Excel, CSV और PDF एक्सपोर्ट',
  'show.l4': 'लाइट और डार्क थीम, किसी भी फ़ोन या कंप्यूटर पर इंस्टॉल करें',
  'show.l5': 'आपका डेटा आपका है — कभी भी पूरा डेटा डाउनलोड करें', 'show.cta': 'मेरा मुफ़्त अकाउंट बनाएं', 'show.alt': 'NijMitra डैशबोर्ड',
  'sec.eyebrow': 'सुरक्षित और निजी', 'sec.title': 'आपकी निजी ज़िंदगी की सुरक्षा के लिए बना',
  's.vault': 'ज़ीरो-नॉलेज पासवर्ड वॉल्ट', 's.vault.d': 'पासवर्ड आपके ब्राउज़र में ही (AES-256) एन्क्रिप्ट होते हैं, जिसकी चाबी सिर्फ़ आपके पास है। सर्वर पर केवल न पढ़ा जा सकने वाला डेटा रहता है।',
  's.2fa': 'टू-स्टेप वेरिफ़िकेशन', 's.2fa.d': 'Google या Microsoft Authenticator और रिकवरी कोड से लॉगिन सुरक्षित करें।',
  's.idle': 'ऑटोमैटिक साइन-आउट', 's.idle.d': 'निष्क्रिय सेशन अपने आप खत्म। हर डिवाइस देखें और एक टैप में दूर से साइन-आउट करें।',
  's.private': 'पूरी तरह निजी', 's.private.d': 'आपकी डायरी और वॉल्ट एडमिन को कभी नहीं दिखते। हर संवेदनशील कार्य रिकॉर्ड होता है।',
  's.enc': 'एन्क्रिप्टेड और एक्सपोर्ट योग्य', 's.enc.d': 'हर जगह HTTPS, संवेदनशील जानकारी एन्क्रिप्टेड, और एक क्लिक में पूरा डेटा एक्सपोर्ट।',
  's.only': 'आपका डेटा सिर्फ़ आपके लिए', 's.only.d': 'हर रिकॉर्ड आपके अकाउंट से जुड़ा है और सर्वर पर जांचा जाता है — कोई और नहीं खोल सकता।',
  'how.eyebrow': 'शुरू करना आसान है', 'how.title': 'सिर्फ़ 3 आसान स्टेप में शुरू करें',
  'how.1': 'रजिस्टर', 'how.1.d': 'अपने मोबाइल नंबर और पासवर्ड से अकाउंट बनाएं और एक सुरक्षा PIN सेट करें।',
  'how.2': 'सुरक्षित करें', 'how.2.d': 'Settings में टू-स्टेप वेरिफ़िकेशन चालू करें और अपना पासवर्ड वॉल्ट बनाएं।',
  'how.3': 'व्यवस्थित करें', 'how.3.d': 'खर्च, दस्तावेज़, संपर्क, उधार और रिमाइंडर जोड़ना शुरू करें — डैशबोर्ड पर सब दिखेगा।',
  'apps.eyebrow': 'मोबाइल, टैबलेट और कंप्यूटर पर', 'apps.title': 'हर डिवाइस पर NijMitra',
  'apps.text': 'Android ऐप डाउनलोड करें, या किसी भी ब्राउज़र में nijmitra.com खोलकर “Install” / “Add to Home Screen” चुनें — यह अपने आइकन और नोटिफ़िकेशन के साथ सामान्य ऐप की तरह चलता है।',
  'apps.web': 'वेब ऐप', 'apps.web.d': 'कोई भी आधुनिक ब्राउज़र', 'apps.android': 'Android', 'apps.android.d': 'Chrome → Install app', 'apps.android.play': 'Google Play पर पाएं',
  'apps.ios': 'iPhone और iPad', 'apps.ios.d': 'Safari → Add to Home Screen', 'apps.desktop': 'डेस्कटॉप', 'apps.desktop.d': 'Chrome / Edge → Install',
  'cta.title': 'आज ही जुड़ें NijMitra के साथ', 'cta.text': 'आपका व्यक्तिगत डिजिटल साथी — आसान, सुरक्षित और हमेशा आपके साथ।', 'cta.register': 'मुफ़्त रजिस्टर करें',
  'footer.works': 'मोबाइल, टैबलेट और डेस्कटॉप पर चलता है', 'footer.contact': 'संपर्क',
  'auth.title1': 'अपनी पूरी', 'auth.titleHl': 'निजी दुनिया', 'auth.title2': 'अब एक ही App में',
  'auth.sub': 'आपका व्यक्तिगत डिजिटल साथी — आपका पैसा, रिकॉर्ड और रिमाइंडर, सुरक्षित एक ही जगह।',
  'auth.f1': 'पैसा, बजट और उधार', 'auth.f1.d': 'हर रुपये, बिल और उधार का हिसाब एक जगह।',
  'auth.f2': 'ज़ीरो-नॉलेज पासवर्ड वॉल्ट', 'auth.f2.d': 'आपके डिवाइस पर एन्क्रिप्टेड — हम भी नहीं पढ़ सकते।',
  'auth.f3': 'मज़बूत सुरक्षा', 'auth.f3.d': 'टू-स्टेप वेरिफ़िकेशन, ऑटो साइन-आउट और पूरा ऑडिट रिकॉर्ड।',
  'auth.f4': 'डायरी, दस्तावेज़ और रिमाइंडर', 'auth.f4.d': 'आपकी निजी ज़िंदगी, व्यवस्थित और खोजने योग्य।',
  'auth.f5': 'दोस्तों से निजी चैट', 'auth.f5.d': 'NijMitra ID या इनवाइट कोड से जुड़ें।',
  'login.title': 'फिर से स्वागत है', 'login.sub': 'अपने मोबाइल नंबर और पासवर्ड से साइन इन करें।', 'login.mobile': 'मोबाइल नंबर', 'login.password': 'पासवर्ड',
  'login.forgot': 'पासवर्ड भूल गए?', 'login.submit': 'साइन इन', 'login.new': 'NijMitra पर नए हैं?', 'login.create': 'अपना अकाउंट बनाएं',
  'login.mobileErr': 'अपना 10 अंकों का मोबाइल नंबर डालें', 'login.passwordErr': 'अपना पासवर्ड डालें', 'login.showPw': 'पासवर्ड दिखाएं',
  'login.reason.idle': 'कुछ समय तक कोई गतिविधि न होने के कारण आप साइन-आउट हो गए।', 'login.reason.expired': 'आपका सेशन समाप्त हो गया। कृपया फिर से साइन इन करें।',
  'login.reason.inactive': 'आपका अकाउंट सक्रिय नहीं है। कृपया सपोर्ट से संपर्क करें।', 'login.registered': 'अकाउंट बन गया। आगे बढ़ने के लिए साइन इन करें।',
  'otp.title': 'टू-स्टेप वेरिफ़िकेशन', 'otp.recoveryHelp': 'अपने सेव किए गए रिकवरी कोड में से एक डालें।',
  'otp.help': 'Google Authenticator या Microsoft Authenticator खोलें और NijMitra का 6 अंकों का कोड डालें।',
  'otp.submit': 'वेरिफ़ाई करें और साइन इन करें', 'otp.useApp': 'Authenticator कोड इस्तेमाल करें', 'otp.useRecovery': 'रिकवरी कोड इस्तेमाल करें', 'otp.back': 'वापस',
  'setup.title': 'टू-स्टेप वेरिफ़िकेशन सेट करें',
  'reg.title': 'अपना अकाउंट बनाएं', 'reg.sub': 'एक मिनट से भी कम समय लगता है। साइन इन के बाद टू-स्टेप वेरिफ़िकेशन चालू कर सकते हैं।',
  'reg.name': 'पूरा नाम', 'reg.mobile': 'मोबाइल नंबर', 'reg.otp': 'मोबाइल OTP', 'reg.sendOtp': 'OTP भेजें', 'reg.resend': 'फिर भेजें',
  'reg.otpPh': '6 अंकों का कोड डालें', 'reg.otpPhFirst': 'पहले "OTP भेजें" दबाएं', 'reg.password': 'पासवर्ड', 'reg.confirm': 'पासवर्ड दोबारा डालें',
  'reg.pin': 'व्यक्तिगत सुरक्षा PIN', 'reg.pinHelp': '(4–6 अंक, संवेदनशील कार्यों की पुष्टि के लिए)',
  'reg.agree1': 'मैं सहमत हूं —', 'reg.terms': 'उपयोग की शर्तें', 'reg.and': 'और', 'reg.privacy': 'गोपनीयता नीति', 'reg.submit': 'अकाउंट बनाएं',
  'reg.have': 'पहले से अकाउंट है?', 'reg.signin': 'साइन इन करें', 'reg.closed': 'नए रजिस्ट्रेशन अभी बंद हैं। कृपया बाद में देखें।', 'reg.back': 'साइन इन पर वापस',
  'reg.e.name': 'अपना पूरा नाम डालें', 'reg.e.mobile': 'सही 10 अंकों का भारतीय मोबाइल नंबर डालें', 'reg.e.pwLen': 'कम से कम 8 अक्षर',
  'reg.e.pwMix': 'इनमें से 3 का उपयोग करें: बड़ा अक्षर, छोटा अक्षर, अंक, चिन्ह', 'reg.e.pin': 'PIN 4–6 अंकों का होना चाहिए', 'reg.e.terms': 'आगे बढ़ने के लिए शर्तें स्वीकार करें',
  'reg.e.match': 'पासवर्ड मेल नहीं खाते', 'reg.e.otp': 'मोबाइल पर भेजा गया कोड डालें',
  'fp.title': 'पासवर्ड रीसेट करें', 'fp.sub': 'हम आपके रजिस्टर्ड मोबाइल नंबर या ईमेल पर एक वेरिफ़िकेशन कोड भेजेंगे।', 'fp.mobile': 'मोबाइल नंबर',
  'fp.send': 'कोड भेजें', 'fp.code': 'वेरिफ़िकेशन कोड', 'fp.new': 'नया पासवर्ड', 'fp.confirm': 'नया पासवर्ड दोबारा डालें', 'fp.submit': 'नया पासवर्ड सेट करें', 'fp.back': 'साइन इन पर वापस',
}

const DICTS: Record<Lang, Partial<Record<TKey, string>>> = { en, hi }

interface LangCtx { lang: Lang; setLang: (l: Lang) => void; t: (k: TKey) => string; pick: (en: string, hi?: string | null) => string }
const Ctx = createContext<LangCtx>({ lang: 'en', setLang: () => {}, t: (k) => en[k], pick: (e) => e })

export function LanguageProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(detectLanguage)
  useEffect(() => { document.documentElement.lang = lang === 'hi' ? 'hi' : 'en' }, [lang])
  const setLang = useCallback((l: Lang) => { setLangState(l); try { localStorage.setItem(STORAGE_KEY, l) } catch { /* ignore */ } }, [])
  const value = useMemo<LangCtx>(() => ({
    lang, setLang,
    t: (k) => DICTS[lang][k] ?? en[k],
    pick: (e, h) => (lang === 'hi' && h ? h : e),
  }), [lang, setLang])
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

export const useI18n = () => useContext(Ctx)

export function LanguageSelect({ className }: { className?: string }) {
  const { lang, setLang, t } = useI18n()
  const current = LANGUAGES.find((l) => l.code === lang)!
  return (
    <Dropdown align="end" className={className}>
      <Dropdown.Toggle variant="outline-secondary" size="sm" className="d-flex align-items-center gap-1 lang-toggle" aria-label={t('lang.label')}>
        <FiGlobe /> <span>{current.native}</span>
      </Dropdown.Toggle>
      <Dropdown.Menu>
        <Dropdown.Header>{t('lang.label')}</Dropdown.Header>
        {LANGUAGES.map((l) => <Dropdown.Item key={l.code} active={l.code === lang} onClick={() => setLang(l.code)} lang={l.code}>
          <span className="d-inline-block" style={{ width: 18 }}>{l.code === lang && <FiCheck />}</span>{l.native}{l.native !== l.label && <span className="text-muted small ms-2">{l.label}</span>}
        </Dropdown.Item>)}
      </Dropdown.Menu>
    </Dropdown>
  )
}

/* ------------------------------------------------------------------ public site info (cached once per page load) */
export interface FooterLinkInfo { labelEn: string; labelHi?: string | null; url: string }
export interface WebsiteInfo {
  aboutEn: string; aboutHi?: string | null; copyrightName: string; companyLine?: string | null; contactEmail?: string | null; contactPhone?: string | null
  address?: string | null; playStoreUrl?: string | null; columns: { titleEn: string; titleHi?: string | null; links: FooterLinkInfo[] }[]; socialLinks: { platform: string; url: string }[]
  androidPackageName?: string; androidSha256Fingerprints?: string[]
}
export interface SiteInfo { applicationName: string; allowRegistration: boolean; website?: WebsiteInfo }
let siteInfoPromise: Promise<SiteInfo> | null = null
export function useSiteInfo() {
  const [info, setInfo] = useState<SiteInfo | null>(null)
  useEffect(() => {
    siteInfoPromise ??= api.get<SiteInfo>('/api/system/info').then((r) => r.data).catch(() => { siteInfoPromise = null; return { applicationName: 'NijMitra', allowRegistration: true } })
    let alive = true
    void siteInfoPromise.then((d) => { if (alive) setInfo(d) })
    return () => { alive = false }
  }, [])
  return info
}
