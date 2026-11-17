import { useEffect, useState } from 'react'
import { Container, Nav, Navbar } from 'react-bootstrap'
import { Link } from 'react-router-dom'
import {
  FiArrowRight, FiBell, FiBookOpen, FiCalendar, FiCheckCircle, FiClock, FiCloud, FiEyeOff, FiFileText, FiGlobe, FiGrid,
  FiLock, FiMessageCircle, FiMonitor, FiPackage, FiShield, FiSmartphone, FiUserCheck, FiUsers, FiZap
} from 'react-icons/fi'
import { FaAndroid, FaApple, FaWindows } from 'react-icons/fa'
import { RiHandCoinLine, RiWallet3Line } from 'react-icons/ri'
import { Logo, LinkButton } from '../../components/ui'
import { ThemeToggle } from '../../components/Layout'
import { api } from '../../lib/api'

const FEATURES = [
  { icon: <RiWallet3Line />, color: '#F47B20', hi: 'खर्च & आय', en: 'Expenses, income & budget', text: 'हर खर्च जोड़ें, आय जानें, मासिक बजट बनाएं। Category-wise reports, recurring bills and overspending alerts.' },
  { icon: <FiFileText />, color: '#1E88E5', hi: 'दस्तावेज़ वॉल्ट', en: 'Document vault', text: 'Aadhaar, PAN, certificates, bills — folders, tags, versions, preview and secure expiring share links.' },
  { icon: <FiLock />, color: '#7B3FBF', hi: 'पासवर्ड मैनेजर', en: 'Password manager', text: 'अपनी सभी IDs और पासवर्ड सुरक्षित रखें। Encrypted on your device with your own master password.' },
  { icon: <FiUsers />, color: '#3AAA35', hi: 'संपर्क डायरी', en: 'Contact directory', text: 'परिवार, मित्र, ग्राहक, सप्लायर — groups, attachments and a full money history for every person.' },
  { icon: <RiHandCoinLine />, color: '#E6397E', hi: 'उधार प्रबंधन', en: 'Udhar / credit ledger', text: 'किससे लेना है, किसे देना है — partial payments, due-date reminders and person-wise statements.' },
  { icon: <FiCalendar />, color: '#F9A825', hi: 'कैलेंडर & मीटिंग', en: 'Calendar & meetings', text: 'Meetings, birthdays, bill payments and follow-ups in day, week, month and agenda views.' },
  { icon: <FiClock />, color: '#0B3D91', hi: 'स्मार्ट रिमाइंडर', en: 'Smart reminders', text: 'समय पर याद दिलाए — in-app, push, email or SMS, even when the app is closed.' },
  { icon: <FiBookOpen />, color: '#8E44AD', hi: 'डेली डायरी', en: 'Private daily diary', text: 'अपने विचार और रोज़ की बातें लिखें — photos, moods, tags, timeline and archives.' },
  { icon: <FiGrid />, color: '#00A0A0', hi: 'कस्टम टेबल', en: 'Custom tables', text: 'अपनी ज़रूरत के अनुसार खुद का रजिस्टर बनाएं — 16 field types, formulas, Excel import & export.' },
  { icon: <FiPackage />, color: '#D35400', hi: 'प्रोडक्ट रजिस्टर', en: 'Product register', text: 'Medicines, stock or assets — MRP, GST, HSN, expiry tracking, images and Excel/PDF export.' },
  { icon: <FiMessageCircle />, color: '#27AE60', hi: 'दोस्तों से चैट', en: 'Friends & private chat', text: 'NijMitra ID या invite code से जुड़ें — real-time chat, files, read receipts, disappearing messages.' },
  { icon: <FiBell />, color: '#C0392B', hi: 'सूचनाएं', en: 'Notifications', text: 'You decide what reaches you — choose email, SMS, push and in-app alerts for every category.' },
]

const SECURITY = [
  { icon: <FiLock />, title: 'Zero-knowledge password vault', text: 'Passwords are encrypted in your browser (AES-256) with a key only you hold. Our servers store only unreadable ciphertext.' },
  { icon: <FiShield />, title: 'Two-step verification', text: 'Protect sign-in with Google or Microsoft Authenticator plus one-time recovery codes.' },
  { icon: <FiClock />, title: 'Automatic sign-out', text: 'Inactive sessions end automatically. See every device and sign out remotely in one tap.' },
  { icon: <FiEyeOff />, title: 'Private by design', text: 'Your diary and vault are never shown in admin screens. Every sensitive action is audit-logged.' },
  { icon: <FiCloud />, title: 'Encrypted & backed up', text: 'HTTPS everywhere, sensitive fields encrypted at rest, and one-click export of all your data.' },
  { icon: <FiUserCheck />, title: 'Only you see your data', text: 'Every record is tied to your account and checked on the server — no one else can open it.' },
]

export default function LandingPage() {
  const [allowRegistration, setAllowRegistration] = useState(true)
  useEffect(() => { api.get('/api/system/info').then((r) => setAllowRegistration(r.data.allowRegistration)).catch(() => {}) }, [])
  return (
    <div className="landing">
      <Navbar expand="lg" sticky="top" className="landing-nav">
        <Container>
          <Navbar.Brand as={Link} to="/"><Logo height={46} /></Navbar.Brand>
          <Navbar.Toggle aria-controls="lnav" />
          <Navbar.Collapse id="lnav">
            <Nav className="mx-auto">
              <Nav.Link href="#features">Features</Nav.Link>
              <Nav.Link href="#security">Security</Nav.Link>
              <Nav.Link href="#how">How it works</Nav.Link>
              <Nav.Link href="#apps">Apps</Nav.Link>
            </Nav>
            <div className="d-flex align-items-center gap-2 py-2 py-lg-0">
              <ThemeToggle />
              <LinkButton to="/login" variant="outline-primary">Login</LinkButton>
              {allowRegistration && <LinkButton to="/register" variant="gradient">Register</LinkButton>}
            </div>
          </Navbar.Collapse>
        </Container>
      </Navbar>

      <header className="landing-hero">
        <Container>
          <div className="row align-items-center g-5">
            <div className="col-lg-6">
              <span className="hero-pill"><FiZap /> Aapka Personal Digital Saathi</span>
              <h1 className="hero-title">अपनी पूरी <span className="hl">निजी दुनिया</span><br />अब एक ही <span className="app">App</span> में</h1>
              <p className="hero-sub">पैसा, खर्च, दस्तावेज़, पासवर्ड, उधार, डायरी, मीटिंग और दोस्तों से चैट — सब कुछ सुरक्षित, निजी और व्यवस्थित।</p>
              <p className="text-muted">Manage your money, documents, passwords, contacts, Udhar, diary, reminders and private chat from one secure app — on mobile, tablet and computer.</p>
              <div className="d-flex flex-wrap gap-2 my-4">
                {allowRegistration && <LinkButton to="/register" variant="gradient" size="lg" className="px-4">आज ही जुड़ें — Register free <FiArrowRight /></LinkButton>}
                <LinkButton to="/login" variant="outline-primary" size="lg" className="px-4">Login</LinkButton>
              </div>
              <div className="d-flex flex-wrap gap-3 small text-muted">
                <span><FiCheckCircle className="text-brand-green" /> सुरक्षित और निजी</span>
                <span><FiCheckCircle className="text-brand-green" /> Free to start</span>
                <span><FiCheckCircle className="text-brand-green" /> हिंदी + English</span>
              </div>
            </div>
            <div className="col-lg-6">
              <div className="hero-visual"><img src="/branding/poster-1.webp" alt="NijMitra app — all features in one app" loading="eager" /></div>
            </div>
          </div>
        </Container>
      </header>

      <section id="features" className="landing-section">
        <Container>
          <div className="section-head">
            <span className="eyebrow">क्या-क्या कर सकते हैं?</span>
            <h2>Everything your personal life needs</h2>
            <div className="brand-underline mx-auto" />
          </div>
          <div className="row g-3 g-md-4">
            {FEATURES.map((f) => (
              <div className="col-sm-6 col-lg-4 col-xl-3" key={f.en}>
                <div className="feature-card h-100">
                  <span className="feature-icon" style={{ background: f.color }}>{f.icon}</span>
                  <h3>{f.hi}</h3>
                  <div className="feature-en">{f.en}</div>
                  <p>{f.text}</p>
                </div>
              </div>
            ))}
          </div>
        </Container>
      </section>

      <section className="landing-section showcase">
        <Container>
          <div className="row align-items-center g-5">
            <div className="col-lg-7"><img src="/branding/poster-2.webp" alt="NijMitra features overview" className="img-fluid rounded-4 shadow-lg" loading="lazy" /></div>
            <div className="col-lg-5">
              <span className="eyebrow">एक App, सारी सुविधाएं</span>
              <h2 className="mb-3">Stop juggling diaries, spreadsheets and ten different apps</h2>
              <ul className="check-list">
                <li>Global search finds a contact, expense, document or saved login in seconds</li>
                <li>Dashboard shows income, expenses, budget, money to receive and to pay</li>
                <li>Excel, CSV and PDF export for reports, registers and Udhar statements</li>
                <li>Light and dark themes, installable on any phone or computer</li>
                <li>Your data is yours — export everything anytime</li>
              </ul>
              {allowRegistration && <LinkButton to="/register" variant="gradient" className="mt-2">Create my free account <FiArrowRight /></LinkButton>}
            </div>
          </div>
        </Container>
      </section>

      <section id="security" className="landing-section security">
        <Container>
          <div className="section-head">
            <span className="eyebrow">सुरक्षित और निजी</span>
            <h2>Built to protect your personal life</h2>
            <div className="brand-underline mx-auto" />
          </div>
          <div className="row g-4">
            {SECURITY.map((s) => (
              <div className="col-md-6 col-lg-4" key={s.title}>
                <div className="sec-card h-100"><span className="sec-icon">{s.icon}</span><h3>{s.title}</h3><p>{s.text}</p></div>
              </div>
            ))}
          </div>
        </Container>
      </section>

      <section id="how" className="landing-section">
        <Container>
          <div className="section-head">
            <span className="eyebrow">शुरू करना आसान है</span>
            <h2>Get started in 3 simple steps</h2>
            <div className="brand-underline mx-auto" />
          </div>
          <div className="row g-4">
            {[
              ['1', 'Register', 'अपने मोबाइल नंबर और पासवर्ड से अकाउंट बनाएं। Set a personal security PIN.'],
              ['2', 'Secure', 'Settings में Two-step verification चालू करें और अपना Password Vault बनाएं।'],
              ['3', 'Organise', 'खर्च, दस्तावेज़, संपर्क, उधार और रिमाइंडर जोड़ना शुरू करें — dashboard पर सब दिखेगा।'],
            ].map(([n, t, d]) => (
              <div className="col-md-4" key={n}><div className="step-card h-100"><span className="step-num">{n}</span><h3>{t}</h3><p>{d}</p></div></div>
            ))}
          </div>
        </Container>
      </section>

      <section id="apps" className="landing-section apps">
        <Container>
          <div className="section-head">
            <span className="eyebrow">मोबाइल, टैबलेट और कंप्यूटर पर</span>
            <h2>Use NijMitra on every device</h2>
            <p className="text-muted mx-auto" style={{ maxWidth: 640 }}>NijMitra is an installable web app (PWA). Open nijmitra.com and choose “Install” / “Add to Home Screen” — it works like a regular app with its own icon and notifications.</p>
          </div>
          <div className="row g-3 justify-content-center">
            {[
              [<FiGlobe key="w" />, 'Web App', 'Any modern browser'],
              [<FaAndroid key="a" />, 'Android', 'Chrome → Install app'],
              [<FaApple key="i" />, 'iPhone & iPad', 'Safari → Add to Home Screen'],
              [<FaWindows key="d" />, 'Desktop', 'Chrome / Edge → Install'],
            ].map(([icon, t, d]) => (
              <div className="col-6 col-md-3" key={t as string}><div className="platform-card"><span>{icon}</span><strong>{t}</strong><small>{d}</small></div></div>
            ))}
          </div>
        </Container>
      </section>

      <section className="cta-band">
        <Container className="d-flex flex-column flex-lg-row align-items-center justify-content-between gap-3">
          <div>
            <h2 className="mb-1">आज ही जुड़ें NijMitra के साथ</h2>
            <p className="mb-0">Your personal digital companion — simple, secure and always with you.</p>
          </div>
          <div className="d-flex gap-2">
            {allowRegistration && <LinkButton to="/register" variant="light" size="lg" className="fw-semibold text-brand-orange">Register free <FiArrowRight /></LinkButton>}
            <LinkButton to="/login" variant="outline-light" size="lg">Login</LinkButton>
          </div>
        </Container>
      </section>

      <footer className="landing-footer">
        <Container>
          <div className="row g-4 align-items-start">
            <div className="col-lg-5">
              <div className="footer-logo"><Logo height={56} /></div>
              <p className="text-muted small mt-3 mb-0">NijMitra — आपका व्यक्तिगत डिजिटल साथी। A secure personal life-management platform for individuals and families.</p>
            </div>
            <div className="col-6 col-lg-2">
              <div className="footer-title">Product</div>
              <a href="#features">Features</a><a href="#security">Security</a><a href="#apps">Apps</a>
            </div>
            <div className="col-6 col-lg-2">
              <div className="footer-title">Account</div>
              <Link to="/login">Login</Link>{allowRegistration && <Link to="/register">Register</Link>}<Link to="/forgot-password">Forgot password</Link>
            </div>
            <div className="col-lg-3">
              <div className="footer-title">Legal</div>
              <a href="/terms.html">Terms of Use</a><a href="/privacy.html">Privacy Policy</a>
              <div className="small text-muted mt-3"><FiSmartphone /> <FiMonitor /> Works on mobile, tablet & desktop</div>
            </div>
          </div>
          <div className="footer-bottom">
            <span>© {new Date().getFullYear()} NijMitra · <a href="https://nijmitra.com">nijmitra.com</a></span>
            <span>A product by <strong>OneStopFullStop Solution</strong></span>
          </div>
        </Container>
      </footer>
    </div>
  )
}
