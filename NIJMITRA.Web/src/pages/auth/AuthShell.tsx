import type { ReactNode } from 'react'
import { FiBookOpen, FiCreditCard, FiLock, FiMessageCircle, FiShield } from 'react-icons/fi'
import { Link } from 'react-router-dom'
import { Logo } from '../../components/ui'
import { ThemeToggle } from '../../components/Layout'

export default function AuthShell({ children }: { children: ReactNode }) {
  const features = [
    [<FiCreditCard key="a" />, 'Money, budgets & Udhar', 'Track every rupee, bill and loan in one place.'],
    [<FiLock key="b" />, 'Zero-knowledge password vault', 'Encrypted on your device — not even we can read it.'],
    [<FiShield key="c" />, 'Bank-grade security', 'Two-step verification, auto sign-out and full audit trail.'],
    [<FiBookOpen key="d" />, 'Diary, documents & reminders', 'Your private life, organised and searchable.'],
    [<FiMessageCircle key="e" />, 'Private chat with friends', 'Connect by NijMitra ID or invite code.'],
  ] as const
  return (
    <div className="auth-shell">
      <section className="auth-hero">
        <Link to="/" className="d-inline-block bg-white rounded-4 px-3 py-2 align-self-start shadow-sm" style={{ position: 'relative', zIndex: 1 }}><Logo variant="light" height={58} /></Link>
        <div style={{ position: 'relative', zIndex: 1 }}>
          <h1 className="hi" style={{ fontSize: '2.3rem', color: '#fff', fontWeight: 800 }}>अपनी पूरी <span style={{ color: '#FFC93C' }}>निजी दुनिया</span><br />अब एक ही App में</h1>
          <p className="mb-4" style={{ color: '#D6E4FF' }}>Aapka Personal Digital Saathi — your money, records and reminders, safely in one place.</p>
          {features.map(([icon, t, d]) => <div className="auth-feature" key={t}>{icon}<div><div className="fw-semibold text-white">{t}</div><div className="small">{d}</div></div></div>)}
        </div>
        <div className="small" style={{ color: '#B9CCF2', position: 'relative', zIndex: 1 }}>© {new Date().getFullYear()} NijMitra · nijmitra.com · A product by OneStopFullStop Solution</div>
      </section>
      <section className="auth-panel">
        <div className="auth-card">
          <div className="d-flex justify-content-between align-items-center mb-4"><Link to="/" className="d-lg-none"><Logo height={44} /></Link><div className="ms-auto"><ThemeToggle /></div></div>
          {children}
        </div>
      </section>
    </div>
  )
}
