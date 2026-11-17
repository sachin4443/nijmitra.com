import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Badge, Button, ButtonGroup, Dropdown, Form, Spinner } from 'react-bootstrap'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import {
  FiBell, FiBook, FiBookOpen, FiCalendar, FiClock, FiCreditCard, FiFileText, FiGrid, FiHome, FiLock, FiLogOut, FiMenu, FiMessageCircle,
  FiMonitor, FiMoon, FiPackage, FiPlus, FiSearch, FiSettings, FiShield, FiSun, FiTable, FiUser, FiUsers, FiX
} from 'react-icons/fi'
import { RiHandCoinLine } from 'react-icons/ri'
import { api, apiError, passive, setAccessToken } from '../lib/api'
import { useDebounced } from '../lib/hooks'
import { date, money, relative } from '../lib/format'
import { useAuth } from '../contexts/AuthContext'
import { useRealtimeEvent } from '../contexts/RealtimeContext'
import { useTheme, type Theme } from '../contexts/ThemeContext'
import { useToast } from '../contexts/ToastContext'
import { Avatar, Highlight, Logo, Mark } from './ui'
import IdleTimer from './IdleTimer'
import { PwaPrompts } from './Pwa'

export const NAV = [
  { to: '/', label: 'Dashboard', icon: <FiHome />, end: true },
  { to: '/finance', label: 'Finance', icon: <FiCreditCard /> },
  { to: '/documents', label: 'Document Vault', icon: <FiFileText /> },
  { to: '/passwords', label: 'Password Manager', icon: <FiLock /> },
  { to: '/products', label: 'Product Register', icon: <FiPackage /> },
  { to: '/tables', label: 'Custom Tables', icon: <FiTable /> },
  { to: '/contacts', label: 'Contacts', icon: <FiUsers /> },
  { to: '/udhar', label: 'Udhar Manager', icon: <RiHandCoinLine /> },
  { to: '/diary', label: 'Daily Diary', icon: <FiBookOpen /> },
  { to: '/calendar', label: 'Calendar', icon: <FiCalendar /> },
  { to: '/reminders', label: 'Reminders', icon: <FiClock /> },
  { to: '/chat', label: 'Friends & Chat', icon: <FiMessageCircle /> },
  { to: '/profile', label: 'My Profile', icon: <FiUser /> },
  { to: '/settings', label: 'Settings', icon: <FiSettings /> },
]

export function ThemeToggle() {
  const { theme, setTheme } = useTheme()
  const opts: { v: Theme; icon: ReactNode; label: string }[] = [{ v: 'light', icon: <FiSun />, label: 'Light' }, { v: 'dark', icon: <FiMoon />, label: 'Dark' }, { v: 'system', icon: <FiMonitor />, label: 'System' }]
  return (
    <ButtonGroup size="sm" className="theme-toggle" aria-label="Theme">
      {opts.map((o) => <Button key={o.v} variant={theme === o.v ? 'primary' : 'outline-secondary'} onClick={() => setTheme(o.v)} title={`${o.label} theme`} aria-pressed={theme === o.v}>{o.icon}</Button>)}
    </ButtonGroup>
  )
}

interface Hit { module: string; id: string; title: string; subtitle?: string; date?: string; amount?: number; link: string }
interface Group { module: string; label: string; count: number; items: Hit[] }

function GlobalSearch() {
  const [q, setQ] = useState('')
  const [open, setOpen] = useState(false)
  const [groups, setGroups] = useState<Group[]>([])
  const [recent, setRecent] = useState<string[]>([])
  const [module, setModule] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(false)
  const term = useDebounced(q.trim(), 300)
  const nav = useNavigate()
  const box = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const close = (e: MouseEvent) => { if (box.current && !box.current.contains(e.target as Node)) setOpen(false) }
    const key = (e: KeyboardEvent) => { if ((e.ctrlKey || e.metaKey) && e.key === 'k') { e.preventDefault(); box.current?.querySelector('input')?.focus() } }
    document.addEventListener('mousedown', close); document.addEventListener('keydown', key)
    return () => { document.removeEventListener('mousedown', close); document.removeEventListener('keydown', key) }
  }, [])
  useEffect(() => { setPage(1) }, [term, module])
  useEffect(() => {
    if (term.length < 2) { setGroups([]); setTotal(0); return }
    let cancelled = false
    setLoading(true)
    api.get('/api/search', { params: { q: term, modules: module ?? undefined, page, pageSize: module ? 10 : 5 } })
      .then((r) => { if (!cancelled) { setGroups(r.data.groups); setTotal(r.data.totalCount) } })
      .catch(() => {}).finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [term, module, page])
  const loadRecent = () => api.get<string[]>('/api/search/recent').then((r) => setRecent(r.data)).catch(() => {})
  const go = (link: string) => { setOpen(false); setQ(''); nav(link) }
  const single = module ? groups.find((g) => g.module === module) : null

  return (
    <div className="nm-search" ref={box}>
      <FiSearch className="icon" />
      <Form.Control value={q} onChange={(e) => { setQ(e.target.value); setOpen(true) }} onFocus={() => { setOpen(true); void loadRecent() }}
        placeholder="Search everything — contacts, expenses, documents, passwords… (Ctrl+K)" aria-label="Global search" />
      {open && (
        <div className="nm-search-panel p-2">
          {term.length < 2 ? (
            <div className="p-2">
              <div className="small text-muted mb-2 d-flex justify-content-between"><span>Recent searches</span>{recent.length > 0 && <a href="#" onClick={(e) => { e.preventDefault(); void api.delete('/api/search/recent').then(() => setRecent([])) }}>Clear</a>}</div>
              {recent.length === 0 ? <div className="small text-muted">Type at least 2 characters.</div> : recent.map((r) => <span key={r} className="tag clickable" onClick={() => setQ(r)}>{r}</span>)}
            </div>
          ) : (
            <>
              <div className="d-flex flex-wrap gap-1 p-1 mb-1">
                <Badge bg="" className={`clickable ${!module ? 'bg-primary' : 'badge-soft'}`} onClick={() => setModule(null)}>All {loading ? '' : `(${total})`}</Badge>
                {(module ? groups : groups).map((g) => <Badge key={g.module} bg="" className={`clickable ${module === g.module ? 'bg-primary' : 'badge-soft'}`} onClick={() => setModule(g.module)}>{g.label} ({g.count})</Badge>)}
              </div>
              {loading && <div className="p-3 text-center"><Spinner size="sm" /></div>}
              {!loading && groups.length === 0 && <div className="p-3 small text-muted text-center">No results for “{term}”.</div>}
              {!loading && groups.map((g) => (
                <div key={g.module} className="mb-2">
                  <div className="small fw-semibold text-muted px-2 py-1 text-uppercase">{g.label} <span className="fw-normal">· {g.count}</span></div>
                  {g.items.map((h) => (
                    <div key={h.id} className="chat-item rounded border-0 py-2" onClick={() => go(h.link)}>
                      <div className="flex-grow-1 min-w-0">
                        <div className="truncate fw-medium"><Highlight text={h.title} term={term} /></div>
                        <div className="small text-muted truncate">{h.subtitle && <Highlight text={h.subtitle} term={term} />}{h.date && ` · ${date(h.date)}`}</div>
                      </div>
                      {h.amount != null && <span className="small fw-semibold">{money(h.amount)}</span>}
                    </div>
                  ))}
                  {!module && g.count > g.items.length && <Button size="sm" variant="link" onClick={() => setModule(g.module)}>View all {g.count}</Button>}
                </div>
              ))}
              {single && single.count > 10 && (
                <div className="d-flex justify-content-between px-2 small">
                  <Button size="sm" variant="link" disabled={page <= 1} onClick={() => setPage(page - 1)}>‹ Previous</Button>
                  <span className="align-self-center text-muted">Page {page} of {Math.ceil(single.count / 10)}</span>
                  <Button size="sm" variant="link" disabled={page >= Math.ceil(single.count / 10)} onClick={() => setPage(page + 1)}>Next ›</Button>
                </div>
              )}
            </>
          )}
        </div>
      )}
    </div>
  )
}

interface Notification { id: string; title: string; body?: string; link?: string; isRead: boolean; createdAt: string; eventType: string }

function NotificationBell() {
  const [count, setCount] = useState(0)
  const [items, setItems] = useState<Notification[]>([])
  const nav = useNavigate()
  const toast = useToast()
  const refresh = () => api.get('/api/notifications/unread-count', passive).then((r) => setCount(r.data.count)).catch(() => {})
  useEffect(() => { void refresh(); const t = setInterval(refresh, 60000); return () => clearInterval(t) }, [])
  useRealtimeEvent<Notification>('notification', (n) => {
    setCount((c) => c + 1)
    if (n.eventType !== 'chat_message' || !location.pathname.startsWith('/chat')) toast.push('notify', n.title, n.body, n.link ? () => nav(n.link!) : undefined)
  })
  const load = () => api.get('/api/notifications', { params: { pageSize: 8 } }).then((r) => setItems(r.data.items)).catch(() => {})
  const open = async (n: Notification) => {
    if (!n.isRead) { await api.post(`/api/notifications/${n.id}/read`).catch(() => {}); setCount((c) => Math.max(0, c - 1)) }
    if (n.link) nav(n.link)
  }
  return (
    <Dropdown align="end" onToggle={(o) => { if (o) void load() }}>
      <Dropdown.Toggle variant="link" className="text-body position-relative p-2 no-caret" aria-label="Notifications">
        <FiBell size={20} />{count > 0 && <Badge pill bg="danger" className="position-absolute top-0 start-100 translate-middle" style={{ fontSize: '.6rem' }}>{count > 99 ? '99+' : count}</Badge>}
      </Dropdown.Toggle>
      <Dropdown.Menu style={{ width: 360, maxWidth: '92vw' }} className="p-0">
        <div className="d-flex justify-content-between align-items-center p-3 border-bottom"><strong>Notifications</strong>
          <Button size="sm" variant="link" className="p-0" onClick={() => void api.post('/api/notifications/read-all').then(() => { setCount(0); void load() })}>Mark all read</Button></div>
        <div style={{ maxHeight: 380, overflowY: 'auto' }}>
          {items.length === 0 && <div className="p-4 text-center text-muted small">You're all caught up.</div>}
          {items.map((n) => (
            <Dropdown.Item key={n.id} onClick={() => void open(n)} className="py-2 border-bottom text-wrap">
              <div className="d-flex gap-2">{!n.isRead && <span className="mt-2 rounded-circle bg-primary flex-none" style={{ width: 8, height: 8 }} />}
                <div className="min-w-0"><div className="fw-medium small">{n.title}</div>{n.body && <div className="small text-muted">{n.body}</div>}<div className="text-muted" style={{ fontSize: '.7rem' }}>{relative(n.createdAt)}</div></div></div>
            </Dropdown.Item>
          ))}
        </div>
        <Dropdown.Item className="text-center small py-2" onClick={() => nav('/notifications')}>View all notifications</Dropdown.Item>
      </Dropdown.Menu>
    </Dropdown>
  )
}

function QuickAdd() {
  const nav = useNavigate()
  const items = [
    ['Expense', '/finance/expenses?new=1'], ['Income', '/finance/income?new=1'], ['Document', '/documents?new=1'], ['Password', '/passwords?new=1'],
    ['Contact', '/contacts?new=1'], ['Udhar entry', '/udhar?new=1'], ['Diary entry', '/diary/new'], ['Meeting / event', '/calendar?new=1'], ['Reminder', '/reminders?new=1']
  ]
  return (
    <Dropdown align="end">
      <Dropdown.Toggle className="btn-gradient btn-icon no-caret" size="sm" aria-label="Quick add"><FiPlus /> <span className="d-none d-md-inline">Quick Add</span></Dropdown.Toggle>
      <Dropdown.Menu>{items.map(([l, to]) => <Dropdown.Item key={to} onClick={() => nav(to!)}>{l}</Dropdown.Item>)}</Dropdown.Menu>
    </Dropdown>
  )
}

function ImpersonationBanner() {
  const { me, completeLogin } = useAuth()
  const toast = useToast()
  const nav = useNavigate()
  if (!me?.isImpersonated) return null
  const exit = async () => {
    try {
      const r = await api.post('/api/auth/impersonation/end')
      setAccessToken(r.data.accessToken)
      await completeLogin(r.data.accessToken)
      nav('/admin/users')
    } catch (e) { toast.error(e) }
  }
  return (
    <div className="impersonation-banner">
      <FiShield /> Signed in as {me.fullName} ({me.publicId}) by {me.impersonatorName ?? 'administrator'} — all actions are audited. Vault, chat and security settings are locked.
      <Button size="sm" variant="light" onClick={() => void exit()}>Return to admin</Button>
    </div>
  )
}

export default function Layout({ admin = false, nav = NAV }: { admin?: boolean; nav?: { to: string; label: string; icon: ReactNode; end?: boolean }[] }) {
  const { me, logout, isAdmin } = useAuth()
  const [open, setOpen] = useState(false)
  const loc = useLocation()
  const navigate = useNavigate()
  const toast = useToast()
  useEffect(() => setOpen(false), [loc.pathname])
  useEffect(() => {
    if (me?.mustChangePassword && loc.pathname !== '/settings/security') {
      toast.push('info', 'Please set a new password', 'Your password was reset by support.')
      navigate('/settings/security')
    }
  }, [me?.mustChangePassword, loc.pathname, navigate, toast])
  return (
    <div className="nm-app">
      <aside className={`nm-sidebar ${admin ? 'admin-sidebar' : ''} ${open ? 'open' : ''}`}>
        <div className="brand justify-content-between"><NavLink to="/"><Logo height={42} /></NavLink><Button variant="link" className="d-lg-none text-body p-1" onClick={() => setOpen(false)} aria-label="Close menu"><FiX /></Button></div>
        <nav>
          {admin && <div className="nm-nav-label">Administration</div>}
          {nav.map((n) => <NavLink key={n.to} to={n.to} end={n.end} className={({ isActive }) => `nm-nav-link ${isActive ? 'active' : ''}`}>{n.icon}<span>{n.label}</span></NavLink>)}
          {!admin && isAdmin && <><div className="nm-nav-label">Platform</div><NavLink to="/admin" className="nm-nav-link"><FiShield /><span>Admin Panel</span></NavLink></>}
          {admin && <><div className="nm-nav-label">My space</div><NavLink to="/" className="nm-nav-link"><FiGrid /><span>Back to my dashboard</span></NavLink></>}
        </nav>
        <div className="p-3 border-top small text-muted d-flex align-items-center gap-2"><FiBook /> <span>v1.0 · <a href="/help/NijMitra-User-Guide.pdf" target="_blank" rel="noreferrer">User guide</a></span></div>
      </aside>
      <div className={`nm-backdrop ${open ? 'show' : ''}`} onClick={() => setOpen(false)} />
      <main className="nm-main">
        <ImpersonationBanner />
        <header className="nm-topbar">
          <Button variant="link" className="d-lg-none text-body p-1" onClick={() => setOpen(true)} aria-label="Open menu"><FiMenu size={22} /></Button>
          <div className="d-lg-none"><Mark size={32} /></div>
          <GlobalSearch />
          <div className="ms-auto d-flex align-items-center gap-2">
            {!admin && <QuickAdd />}
            <div className="d-none d-md-block"><ThemeToggle /></div>
            <NotificationBell />
            <Dropdown align="end">
              <Dropdown.Toggle variant="link" className="text-body p-0 d-flex align-items-center gap-2 text-decoration-none no-caret">
                <Avatar name={me?.fullName} src={me?.profile.avatarFileId ? `/api/me/avatar/${me.id}` : null} size={34} />
                <span className="d-none d-xl-block text-start lh-sm"><div className="fw-semibold small">{me?.fullName}</div><div className="text-muted" style={{ fontSize: '.72rem' }}>{me?.publicId}</div></span>
              </Dropdown.Toggle>
              <Dropdown.Menu>
                <Dropdown.Header>{me?.fullName}<div className="small">{me?.publicId}</div></Dropdown.Header>
                <Dropdown.Item onClick={() => navigate('/profile')}><FiUser className="me-2" />My profile</Dropdown.Item>
                <Dropdown.Item onClick={() => navigate('/settings')}><FiSettings className="me-2" />Settings</Dropdown.Item>
                <Dropdown.Item onClick={() => navigate('/settings/security')}><FiShield className="me-2" />Security</Dropdown.Item>
                {isAdmin && <Dropdown.Item onClick={() => navigate('/admin')}><FiGrid className="me-2" />Admin panel</Dropdown.Item>}
                <div className="px-3 py-2 d-md-none"><ThemeToggle /></div>
                <Dropdown.Divider />
                <Dropdown.Item onClick={() => void logout().then(() => navigate('/login'))} className="text-danger"><FiLogOut className="me-2" />Sign out</Dropdown.Item>
              </Dropdown.Menu>
            </Dropdown>
          </div>
        </header>
        <div className="nm-content"><Outlet /></div>
      </main>
      <IdleTimer />
      <PwaPrompts />
    </div>
  )
}

export function useApiAction() {
  const toast = useToast()
  return async <T,>(fn: () => Promise<T>, success?: string): Promise<T | undefined> => {
    try { const r = await fn(); if (success) toast.success(success); return r }
    catch (e) { toast.push('error', apiError(e).message); return undefined }
  }
}
