import type { ReactNode } from 'react'
import { FiBookOpen, FiCreditCard, FiLock, FiMessageCircle, FiShield } from 'react-icons/fi'
import { Link } from 'react-router-dom'
import { Logo } from '../../components/ui'
import { ThemeToggle } from '../../components/Layout'
import { LanguageSelect, useI18n, useSiteInfo } from '../../lib/i18n'

export default function AuthShell({ children }: { children: ReactNode }) {
  const { t } = useI18n()
  const site = useSiteInfo()
  const features = [
    [<FiCreditCard key="a" />, t('auth.f1'), t('auth.f1.d')],
    [<FiLock key="b" />, t('auth.f2'), t('auth.f2.d')],
    [<FiShield key="c" />, t('auth.f3'), t('auth.f3.d')],
    [<FiBookOpen key="d" />, t('auth.f4'), t('auth.f4.d')],
    [<FiMessageCircle key="e" />, t('auth.f5'), t('auth.f5.d')],
  ] as const
  return (
    <div className="auth-shell">
      <section className="auth-hero">
        <Link to="/" className="d-inline-block bg-white rounded-4 px-3 py-2 align-self-start shadow-sm" style={{ position: 'relative', zIndex: 1 }}><Logo variant="light" height={58} /></Link>
        <div style={{ position: 'relative', zIndex: 1 }}>
          <h1 style={{ fontSize: '2.3rem', color: '#fff', fontWeight: 800 }}>{t('auth.title1')} <span style={{ color: '#FFC93C' }}>{t('auth.titleHl')}</span><br />{t('auth.title2')}</h1>
          <p className="mb-4" style={{ color: '#D6E4FF' }}>{t('auth.sub')}</p>
          {features.map(([icon, t, d]) => <div className="auth-feature" key={t}>{icon}<div><div className="fw-semibold text-white">{t}</div><div className="small">{d}</div></div></div>)}
        </div>
        <div className="small" style={{ color: '#B9CCF2', position: 'relative', zIndex: 1 }}>© {new Date().getFullYear()} {site?.website?.copyrightName ?? 'NijMitra'} · nijmitra.com{site?.website?.companyLine ? ` · ${site.website.companyLine}` : ''}</div>
      </section>
      <section className="auth-panel">
        <div className="auth-card">
          <div className="d-flex justify-content-between align-items-center mb-4"><Link to="/" className="d-lg-none"><Logo height={44} /></Link><div className="ms-auto d-flex align-items-center gap-2"><LanguageSelect /><ThemeToggle /></div></div>
          {children}
        </div>
      </section>
    </div>
  )
}
