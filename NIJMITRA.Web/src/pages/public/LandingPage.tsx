import type { ReactNode } from 'react'
import { Container, Nav, Navbar } from 'react-bootstrap'
import { Link } from 'react-router-dom'
import {
  FiArrowRight, FiBell, FiBookOpen, FiCalendar, FiCheckCircle, FiClock, FiCloud, FiEyeOff, FiFileText, FiGlobe, FiGrid,
  FiLock, FiMail, FiMapPin, FiMessageCircle, FiMonitor, FiPackage, FiPhone, FiShield, FiSmartphone, FiUserCheck, FiUsers, FiZap
} from 'react-icons/fi'
import { FaAndroid, FaApple, FaFacebook, FaGooglePlay, FaInstagram, FaLinkedin, FaTelegram, FaWhatsapp, FaWindows, FaYoutube } from 'react-icons/fa'
import { FaXTwitter } from 'react-icons/fa6'
import { RiHandCoinLine, RiWallet3Line } from 'react-icons/ri'
import { Logo, LinkButton } from '../../components/ui'
import { ThemeToggle } from '../../components/Layout'
import { LanguageSelect, useI18n, useSiteInfo, type TKey } from '../../lib/i18n'

const FEATURES: { icon: ReactNode; color: string; key: TKey }[] = [
  { icon: <RiWallet3Line />, color: '#F47B20', key: 'f.finance' },
  { icon: <FiFileText />, color: '#1E88E5', key: 'f.docs' },
  { icon: <FiLock />, color: '#7B3FBF', key: 'f.pw' },
  { icon: <FiUsers />, color: '#3AAA35', key: 'f.contacts' },
  { icon: <RiHandCoinLine />, color: '#E6397E', key: 'f.udhar' },
  { icon: <FiCalendar />, color: '#F9A825', key: 'f.cal' },
  { icon: <FiClock />, color: '#0B3D91', key: 'f.rem' },
  { icon: <FiBookOpen />, color: '#8E44AD', key: 'f.diary' },
  { icon: <FiGrid />, color: '#00A0A0', key: 'f.tables' },
  { icon: <FiPackage />, color: '#D35400', key: 'f.products' },
  { icon: <FiMessageCircle />, color: '#27AE60', key: 'f.chat' },
  { icon: <FiBell />, color: '#C0392B', key: 'f.notif' },
]

const SECURITY: { icon: ReactNode; key: TKey }[] = [
  { icon: <FiLock />, key: 's.vault' }, { icon: <FiShield />, key: 's.2fa' }, { icon: <FiClock />, key: 's.idle' },
  { icon: <FiEyeOff />, key: 's.private' }, { icon: <FiCloud />, key: 's.enc' }, { icon: <FiUserCheck />, key: 's.only' },
]

const SOCIAL_ICONS: Record<string, ReactNode> = {
  facebook: <FaFacebook />, instagram: <FaInstagram />, x: <FaXTwitter />, youtube: <FaYoutube />, linkedin: <FaLinkedin />,
  whatsapp: <FaWhatsapp />, telegram: <FaTelegram />, website: <FiGlobe />,
}

/** Footer/admin links: in-app routes use the router, #sections and static pages are plain links, external links open in a new tab. */
export function SiteLink({ url, children }: { url: string; children: ReactNode }) {
  if (url.startsWith('/') && !url.includes('.')) return <Link to={url}>{children}</Link>
  if (/^https?:\/\//i.test(url)) return <a href={url} target="_blank" rel="noopener noreferrer">{children}</a>
  return <a href={url}>{children}</a>
}

/** English hero visual built from a real app screen (the illustrated posters are in Hindi). */
function HeroPhone() {
  const { t } = useI18n()
  const chips: [ReactNode, string, TKey][] = [
    [<RiWallet3Line key="1" />, '#F47B20', 'f.finance'], [<FiFileText key="2" />, '#1E88E5', 'f.docs'], [<FiLock key="3" />, '#7B3FBF', 'f.pw'],
    [<RiHandCoinLine key="4" />, '#E6397E', 'f.udhar'], [<FiCalendar key="5" />, '#F9A825', 'f.cal'], [<FiMessageCircle key="6" />, '#27AE60', 'f.chat'],
  ]
  return (
    <div className="hero-phone-wrap" aria-label={t('hero.alt')} role="img">
      <div className="hero-blob" />
      <div className="hero-phone"><img src="/branding/screens/mobile-dashboard.webp" alt="" loading="eager" width={540} height={743} /></div>
      <div className="hero-chips">
        {chips.map(([icon, color, key], i) => <span key={key} className={`hero-chip c${i}`}><span className="ic" style={{ background: color }}>{icon}</span>{t(key)}</span>)}
      </div>
    </div>
  )
}

export default function LandingPage() {
  const { t, lang, pick } = useI18n()
  const info = useSiteInfo()
  const allowRegistration = info?.allowRegistration ?? true
  const web = info?.website
  const hindi = lang === 'hi'
  return (
    <div className="landing">
      <Navbar expand="lg" sticky="top" className="landing-nav">
        <Container>
          <Navbar.Brand as={Link} to="/"><Logo height={46} /></Navbar.Brand>
          <div className="d-flex align-items-center gap-2 d-lg-none ms-auto me-2"><LanguageSelect /></div>
          <Navbar.Toggle aria-controls="lnav" />
          <Navbar.Collapse id="lnav">
            <Nav className="mx-auto">
              <Nav.Link href="#features">{t('nav.features')}</Nav.Link>
              <Nav.Link href="#security">{t('nav.security')}</Nav.Link>
              <Nav.Link href="#how">{t('nav.how')}</Nav.Link>
              <Nav.Link href="#apps">{t('nav.apps')}</Nav.Link>
            </Nav>
            <div className="d-flex align-items-center gap-2 py-2 py-lg-0">
              <LanguageSelect className="d-none d-lg-block" />
              <ThemeToggle />
              <LinkButton to="/login" variant="outline-primary">{t('nav.login')}</LinkButton>
              {allowRegistration && <LinkButton to="/register" variant="gradient">{t('nav.register')}</LinkButton>}
            </div>
          </Navbar.Collapse>
        </Container>
      </Navbar>

      <header className="landing-hero">
        <Container>
          <div className="row align-items-center g-5">
            <div className="col-lg-6">
              <span className="hero-pill"><FiZap /> {t('hero.pill')}</span>
              <h1 className="hero-title">{t('hero.title1')} <span className="hl">{t('hero.titleHl')}</span><br />{t('hero.title2')} <span className="app">{t('hero.titleApp')}</span></h1>
              <p className="hero-sub">{t('hero.sub')}</p>
              <p className="text-muted">{t('hero.sub2')}</p>
              <div className="d-flex flex-wrap gap-2 my-4">
                {allowRegistration && <LinkButton to="/register" variant="gradient" size="lg" className="px-4">{t('hero.cta')} <FiArrowRight /></LinkButton>}
                <LinkButton to="/login" variant="outline-primary" size="lg" className="px-4">{t('nav.login')}</LinkButton>
              </div>
              <div className="d-flex flex-wrap gap-3 small text-muted">
                <span><FiCheckCircle className="text-brand-green" /> {t('hero.b1')}</span>
                <span><FiCheckCircle className="text-brand-green" /> {t('hero.b2')}</span>
                <span><FiCheckCircle className="text-brand-green" /> {t('hero.b3')}</span>
              </div>
            </div>
            <div className="col-lg-6">
              {hindi ? <div className="hero-visual"><img src="/branding/poster-1.webp" alt={t('hero.alt')} loading="eager" /></div> : <HeroPhone />}
            </div>
          </div>
        </Container>
      </header>

      <section id="features" className="landing-section">
        <Container>
          <div className="section-head">
            <span className="eyebrow">{t('features.eyebrow')}</span>
            <h2>{t('features.title')}</h2>
            <div className="brand-underline mx-auto" />
          </div>
          <div className="row g-3 g-md-4">
            {FEATURES.map((f) => (
              <div className="col-sm-6 col-lg-4 col-xl-3" key={f.key}>
                <div className="feature-card h-100">
                  <span className="feature-icon" style={{ background: f.color }}>{f.icon}</span>
                  <h3>{t(f.key)}</h3>
                  <p>{t(`${f.key}.d` as TKey)}</p>
                </div>
              </div>
            ))}
          </div>
        </Container>
      </section>

      <section className="landing-section showcase">
        <Container>
          <div className="row align-items-center g-5">
            <div className="col-lg-7">
              {hindi ? <img src="/branding/poster-2.webp" alt={t('show.alt')} className="img-fluid rounded-4 shadow-lg" loading="lazy" />
                : <div className="browser-frame shadow-lg"><div className="bar"><span /><span /><span /><em>nijmitra.com</em></div><img src="/branding/screens/dashboard.webp" alt={t('show.alt')} loading="lazy" width={1600} height={1000} /></div>}
            </div>
            <div className="col-lg-5">
              <span className="eyebrow">{t('show.eyebrow')}</span>
              <h2 className="mb-3">{t('show.title')}</h2>
              <ul className="check-list">{(['show.l1', 'show.l2', 'show.l3', 'show.l4', 'show.l5'] as TKey[]).map((k) => <li key={k}>{t(k)}</li>)}</ul>
              {allowRegistration && <LinkButton to="/register" variant="gradient" className="mt-2">{t('show.cta')} <FiArrowRight /></LinkButton>}
            </div>
          </div>
        </Container>
      </section>

      <section id="security" className="landing-section security">
        <Container>
          <div className="section-head">
            <span className="eyebrow">{t('sec.eyebrow')}</span>
            <h2>{t('sec.title')}</h2>
            <div className="brand-underline mx-auto" />
          </div>
          <div className="row g-4">
            {SECURITY.map((s) => (
              <div className="col-md-6 col-lg-4" key={s.key}>
                <div className="sec-card h-100"><span className="sec-icon">{s.icon}</span><h3>{t(s.key)}</h3><p>{t(`${s.key}.d` as TKey)}</p></div>
              </div>
            ))}
          </div>
        </Container>
      </section>

      <section id="how" className="landing-section">
        <Container>
          <div className="section-head">
            <span className="eyebrow">{t('how.eyebrow')}</span>
            <h2>{t('how.title')}</h2>
            <div className="brand-underline mx-auto" />
          </div>
          <div className="row g-4">
            {(['how.1', 'how.2', 'how.3'] as TKey[]).map((k, i) => (
              <div className="col-md-4" key={k}><div className="step-card h-100"><span className="step-num">{i + 1}</span><h3>{t(k)}</h3><p>{t(`${k}.d` as TKey)}</p></div></div>
            ))}
          </div>
        </Container>
      </section>

      <section id="apps" className="landing-section apps">
        <Container>
          <div className="section-head">
            <span className="eyebrow">{t('apps.eyebrow')}</span>
            <h2>{t('apps.title')}</h2>
            <p className="text-muted mx-auto" style={{ maxWidth: 640 }}>{t('apps.text')}</p>
          </div>
          <div className="row g-3 justify-content-center">
            <div className="col-6 col-md-3"><div className="platform-card"><span><FiGlobe /></span><strong>{t('apps.web')}</strong><small>{t('apps.web.d')}</small></div></div>
            <div className="col-6 col-md-3">{web?.playStoreUrl
              ? <a className="platform-card text-decoration-none text-reset d-flex" href={web.playStoreUrl} target="_blank" rel="noopener noreferrer"><span><FaAndroid /></span><strong>{t('apps.android')}</strong><small className="text-brand-orange fw-semibold"><FaGooglePlay /> {t('apps.android.play')}</small></a>
              : <div className="platform-card"><span><FaAndroid /></span><strong>{t('apps.android')}</strong><small>{t('apps.android.d')}</small></div>}</div>
            <div className="col-6 col-md-3"><div className="platform-card"><span><FaApple /></span><strong>{t('apps.ios')}</strong><small>{t('apps.ios.d')}</small></div></div>
            <div className="col-6 col-md-3"><div className="platform-card"><span><FaWindows /></span><strong>{t('apps.desktop')}</strong><small>{t('apps.desktop.d')}</small></div></div>
          </div>
        </Container>
      </section>

      <section className="cta-band">
        <Container className="d-flex flex-column flex-lg-row align-items-center justify-content-between gap-3">
          <div>
            <h2 className="mb-1">{t('cta.title')}</h2>
            <p className="mb-0">{t('cta.text')}</p>
          </div>
          <div className="d-flex gap-2">
            {allowRegistration && <LinkButton to="/register" variant="light" size="lg" className="fw-semibold text-brand-orange">{t('cta.register')} <FiArrowRight /></LinkButton>}
            <LinkButton to="/login" variant="outline-light" size="lg">{t('nav.login')}</LinkButton>
          </div>
        </Container>
      </section>

      <footer className="landing-footer">
        <Container>
          <div className="row g-4 align-items-start">
            <div className="col-lg-4">
              <div className="footer-logo"><Logo height={56} /></div>
              {web && <p className="text-muted small mt-3 mb-2">{pick(web.aboutEn, web.aboutHi)}</p>}
              {web && (web.contactEmail || web.contactPhone || web.address) && <div className="small footer-contact">
                {web.contactEmail && <a href={`mailto:${web.contactEmail}`}><FiMail /> {web.contactEmail}</a>}
                {web.contactPhone && <a href={`tel:${web.contactPhone.replace(/[^\d+]/g, '')}`}><FiPhone /> {web.contactPhone}</a>}
                {web.address && <span className="text-muted"><FiMapPin /> {web.address}</span>}
              </div>}
              {web && web.socialLinks.length > 0 && <div className="footer-social">{web.socialLinks.map((s, i) => <a key={i} href={s.url} target="_blank" rel="noopener noreferrer" aria-label={s.platform}>{SOCIAL_ICONS[s.platform] ?? <FiGlobe />}</a>)}</div>}
            </div>
            {web?.columns.map((c, i) => (
              <div className="col-6 col-lg" key={i}>
                <div className="footer-title">{pick(c.titleEn, c.titleHi)}</div>
                {c.links.filter((l) => allowRegistration || l.url !== '/register').map((l, j) => <SiteLink key={j} url={l.url}>{pick(l.labelEn, l.labelHi)}</SiteLink>)}
              </div>
            ))}
          </div>
          <div className="small text-muted mt-4"><FiSmartphone /> <FiMonitor /> {t('footer.works')}</div>
          <div className="footer-bottom">
            <span>© {new Date().getFullYear()} {web?.copyrightName ?? 'NijMitra'} · <a href="https://nijmitra.com">nijmitra.com</a></span>
            {web?.companyLine && <span>{web.companyLine}</span>}
          </div>
        </Container>
      </footer>
    </div>
  )
}
