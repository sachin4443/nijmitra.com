import { useCallback, useState } from 'react'
import { Alert, Badge, Button, Card, Form, Modal, Nav, Spinner, Table } from 'react-bootstrap'
import { FiBell, FiDownload, FiKey, FiLogOut, FiMonitor, FiShield, FiSmartphone, FiTrash2, FiUser } from 'react-icons/fi'
import { NavLink, useParams } from 'react-router-dom'
import { api, apiError, downloadFile } from '../../lib/api'
import { useAsync } from '../../lib/hooks'
import { dateTime, relative } from '../../lib/format'
import { useAuth } from '../../contexts/AuthContext'
import { useTheme } from '../../contexts/ThemeContext'
import { useToast } from '../../contexts/ToastContext'
import { ErrorBox, Loading, PageHeader, Pager, useConfirm, useReauth, withReauth } from '../../components/ui'
import { ThemeToggle } from '../../components/Layout'
import { enablePush } from '../../components/Pwa'
import { RecoveryCodes, TotpSetup } from './TwoFactor'

export default function SettingsPage() {
  const { tab = 'general' } = useParams()
  const tabs = [['general', 'General', <FiUser key="g" />], ['security', 'Security', <FiShield key="s" />], ['devices', 'Devices & sessions', <FiMonitor key="d" />], ['notifications', 'Notifications', <FiBell key="n" />], ['data', 'Data & account', <FiDownload key="x" />]] as const
  return (
    <>
      <PageHeader title="Settings" subtitle="Security, devices, notifications and your data" />
      <Nav variant="pills" className="mb-3 gap-1 flex-nowrap overflow-auto">
        {tabs.map(([k, l, i]) => <Nav.Item key={k}><Nav.Link as={NavLink} to={`/settings/${k}`} className="d-flex align-items-center gap-2 text-nowrap">{i}{l}</Nav.Link></Nav.Item>)}
      </Nav>
      {tab === 'general' && <GeneralSettings />}
      {tab === 'security' && <SecuritySettings />}
      {tab === 'devices' && <DevicesSettings />}
      {tab === 'notifications' && <NotificationSettings />}
      {tab === 'data' && <DataSettings />}
    </>
  )
}

function GeneralSettings() {
  const { theme } = useTheme()
  return (
    <Card><Card.Body>
      <h6 className="fw-bold">Appearance</h6>
      <p className="small text-muted">Choose light, dark, or follow your device setting. Current: <strong>{theme}</strong>.</p>
      <ThemeToggle />
      <hr />
      <h6 className="fw-bold">Install the app</h6>
      <p className="small text-muted mb-0">On Android/desktop Chrome or Edge use the “Install” icon in the address bar. On iPhone open in Safari → Share → “Add to Home Screen”.</p>
    </Card.Body></Card>
  )
}

function SecuritySettings() {
  const { me, reloadMe } = useAuth()
  const toast = useToast()
  const [mode, setMode] = useState<'none' | 'enable' | 'codes' | 'disable' | 'regen'>('none')
  const [codes, setCodes] = useState<string[]>([])
  const [pw, setPw] = useState({ currentPassword: '', newPassword: '', confirmPassword: '' })
  const [pin, setPin] = useState({ currentPassword: '', newPin: '' })
  const [dis, setDis] = useState({ password: '', code: '' })
  const [email, setEmail] = useState('')
  const [emailCode, setEmailCode] = useState('')
  const [busy, setBusy] = useState('')
  const run = async (key: string, fn: () => Promise<unknown>, ok: string) => {
    setBusy(key)
    try { await fn(); toast.success(ok); await reloadMe() } catch (e) { toast.error(e) } finally { setBusy('') }
  }
  const begin = useCallback(() => api.post('/api/auth/2fa/setup').then((r) => r.data), [])
  if (!me) return null
  return (
    <div className="row g-3">
      <div className="col-lg-6">
        <Card className="h-100"><Card.Body>
          <div className="d-flex justify-content-between align-items-start">
            <div><h6 className="fw-bold mb-1"><FiShield className="me-2" />Two-step verification</h6>
              <p className="small text-muted">Require a code from your authenticator app at every sign-in. Strongly recommended.</p></div>
            <Badge bg="" className={me.twoFactorEnabled ? 'bg-soft-success' : 'bg-soft-warning'}>{me.twoFactorEnabled ? 'Enabled' : 'Off'}</Badge>
          </div>
          {mode === 'none' && (!me.twoFactorEnabled
            ? <Button onClick={() => setMode('enable')}>Set up authenticator</Button>
            : <div className="d-flex gap-2 flex-wrap"><Button variant="outline-primary" onClick={() => setMode('regen')}>New recovery codes</Button><Button variant="outline-danger" onClick={() => setMode('disable')}>Turn off</Button></div>)}
          {mode === 'enable' && <TotpSetup begin={begin} confirm={async (c) => { const r = await api.post('/api/auth/2fa/enable', { code: c }); setCodes(r.data.codes); setMode('codes') }} />}
          {mode === 'codes' && <RecoveryCodes codes={codes} onDone={() => { setMode('none'); void reloadMe() }} />}
          {(mode === 'disable' || mode === 'regen') && (
            <Form onSubmit={(e) => {
              e.preventDefault()
              if (mode === 'disable') void run('2fa', () => api.post('/api/auth/2fa/disable', dis), 'Two-step verification turned off').then(() => setMode('none'))
              else void api.post('/api/auth/2fa/recovery-codes', { code: dis.code }).then((r) => { setCodes(r.data.codes); setMode('codes') }).catch((x) => toast.error(x))
            }}>
              {mode === 'disable' && <Form.Control type="password" className="mb-2" placeholder="Account password" value={dis.password} onChange={(e) => setDis({ ...dis, password: e.target.value })} />}
              <Form.Control className="mb-2 secret" inputMode="numeric" placeholder="Authenticator code" value={dis.code} onChange={(e) => setDis({ ...dis, code: e.target.value.replace(/\D/g, '').slice(0, 6) })} />
              <div className="d-flex gap-2"><Button type="submit" variant={mode === 'disable' ? 'danger' : 'primary'}>{mode === 'disable' ? 'Turn off' : 'Generate'}</Button><Button variant="link" onClick={() => setMode('none')}>Cancel</Button></div>
            </Form>
          )}
        </Card.Body></Card>
      </div>
      <div className="col-lg-6">
        <Card className="h-100"><Card.Body>
          <h6 className="fw-bold"><FiKey className="me-2" />Change password</h6>
          {me.mustChangePassword && <Alert variant="warning" className="small py-2">Your password was reset by support. Please choose a new one now.</Alert>}
          <Form onSubmit={(e) => { e.preventDefault(); void run('pw', () => api.post('/api/auth/change-password', pw), 'Password changed. Other devices were signed out.').then(() => setPw({ currentPassword: '', newPassword: '', confirmPassword: '' })) }}>
            <Form.Control type="password" className="mb-2" placeholder="Current password" autoComplete="current-password" value={pw.currentPassword} onChange={(e) => setPw({ ...pw, currentPassword: e.target.value })} />
            <Form.Control type="password" className="mb-2" placeholder="New password" autoComplete="new-password" value={pw.newPassword} onChange={(e) => setPw({ ...pw, newPassword: e.target.value })} />
            <Form.Control type="password" className="mb-2" placeholder="Confirm new password" autoComplete="new-password" value={pw.confirmPassword} onChange={(e) => setPw({ ...pw, confirmPassword: e.target.value })} />
            <Button type="submit" disabled={busy === 'pw'}>{busy === 'pw' ? <Spinner size="sm" /> : 'Update password'}</Button>
          </Form>
        </Card.Body></Card>
      </div>
      <div className="col-lg-6">
        <Card className="h-100"><Card.Body>
          <h6 className="fw-bold">Security PIN</h6>
          <p className="small text-muted">Your PIN confirms sensitive actions like data export and vault backups.</p>
          <Form onSubmit={(e) => { e.preventDefault(); void run('pin', () => api.post('/api/auth/change-pin', pin), 'PIN updated').then(() => setPin({ currentPassword: '', newPin: '' })) }}>
            <Form.Control type="password" className="mb-2" placeholder="Account password" value={pin.currentPassword} onChange={(e) => setPin({ ...pin, currentPassword: e.target.value })} />
            <Form.Control type="password" inputMode="numeric" maxLength={6} className="mb-2" placeholder="New PIN (4–6 digits)" value={pin.newPin} onChange={(e) => setPin({ ...pin, newPin: e.target.value.replace(/\D/g, '') })} />
            <Button type="submit" disabled={busy === 'pin'}>Update PIN</Button>
          </Form>
        </Card.Body></Card>
      </div>
      <div className="col-lg-6">
        <Card className="h-100"><Card.Body>
          <h6 className="fw-bold">Email address</h6>
          <p className="small mb-2">{me.email ? <>{me.email} {me.emailVerified ? <Badge bg="" className="bg-soft-success">Verified</Badge> : <Badge bg="" className="bg-soft-warning">Not verified</Badge>}</> : <span className="text-muted">No email added yet.</span>}</p>
          {me.pendingEmail && <Alert variant="info" className="small py-2">Verification pending for <strong>{me.pendingEmail}</strong>.</Alert>}
          <Form className="d-flex gap-2 mb-2" onSubmit={(e) => { e.preventDefault(); setBusy('email'); api.post('/api/auth/email', { email }).then((r) => { toast.success(r.data.message); setEmail(''); void reloadMe() }).catch((x) => toast.error(x)).finally(() => setBusy('')) }}>
            <Form.Control type="email" placeholder="name@example.com" value={email} onChange={(e) => setEmail(e.target.value)} />
            <Button type="submit" disabled={!email || busy === 'email'}>Save</Button>
          </Form>
          {(me.pendingEmail || (me.email && !me.emailVerified)) && (
            <Form className="d-flex gap-2" onSubmit={(e) => { e.preventDefault(); void run('ev', () => api.post('/api/auth/email/verify', { code: emailCode }), 'Email verified') }}>
              <Form.Control inputMode="numeric" placeholder="Verification code" value={emailCode} onChange={(e) => setEmailCode(e.target.value)} />
              <Button type="submit" variant="outline-primary">Verify</Button>
              <Button variant="link" onClick={() => void api.post('/api/auth/email/resend').then((r) => toast.success(r.data.message)).catch((x) => toast.error(x))}>Resend</Button>
            </Form>
          )}
        </Card.Body></Card>
      </div>
    </div>
  )
}

interface SessionRow { id: string; createdAt: string; lastActivityAt: string; ipAddress?: string; browser?: string; operatingSystem?: string; deviceName?: string; isCurrent: boolean; isImpersonation: boolean }
interface DeviceRow { id: string; name?: string; browser?: string; operatingSystem?: string; deviceType?: string; firstSeenAt: string; lastSeenAt: string; lastIpAddress?: string }
interface HistoryRow { id: string; event: string; success: boolean; failureReason?: string; ipAddress?: string; browser?: string; operatingSystem?: string; timestamp: string }

function DevicesSettings() {
  const toast = useToast()
  const confirm = useConfirm()
  const [page, setPage] = useState(1)
  const sessions = useAsync(() => api.get<SessionRow[]>('/api/auth/sessions').then((r) => r.data), [])
  const devices = useAsync(() => api.get<DeviceRow[]>('/api/auth/devices').then((r) => r.data), [])
  const history = useAsync(() => api.get('/api/auth/login-history', { params: { page, pageSize: 15 } }).then((r) => r.data as { items: HistoryRow[]; totalPages: number }), [page])
  const act = async (fn: () => Promise<unknown>, msg: string) => { try { await fn(); toast.success(msg); void sessions.reload(); void devices.reload() } catch (e) { toast.error(e) } }
  return (
    <div className="row g-3">
      <div className="col-xl-6"><Card className="h-100">
        <Card.Header className="d-flex justify-content-between align-items-center">Active sessions
          <Button size="sm" variant="outline-danger" onClick={async () => { if (await confirm({ title: 'Sign out all other devices?', confirmText: 'Sign out others' })) void act(() => api.post('/api/auth/sessions/revoke-others'), 'Other devices signed out') }}><FiLogOut /> Sign out others</Button></Card.Header>
        <Card.Body>{sessions.loading ? <Loading /> : sessions.data?.map((s) => (
          <div key={s.id} className="list-row">
            <FiMonitor className="text-primary" />
            <div className="flex-grow-1 min-w-0"><div className="fw-medium">{s.browser ?? 'Browser'} · {s.operatingSystem ?? 'Unknown OS'} {s.isCurrent && <Badge bg="" className="bg-soft-success">This device</Badge>} {s.isImpersonation && <Badge bg="danger">Support session</Badge>}</div>
              <div className="small text-muted">{s.ipAddress} · active {relative(s.lastActivityAt)} · since {dateTime(s.createdAt)}</div></div>
            {!s.isCurrent && <Button size="sm" variant="link" className="text-danger" onClick={() => void act(() => api.delete(`/api/auth/sessions/${s.id}`), 'Session ended')}>End</Button>}
          </div>))}</Card.Body>
      </Card></div>
      <div className="col-xl-6"><Card className="h-100">
        <Card.Header>Known devices</Card.Header>
        <Card.Body>{devices.loading ? <Loading /> : devices.data?.length === 0 ? <div className="small text-muted">No devices yet.</div> : devices.data?.map((d) => (
          <div key={d.id} className="list-row">
            {d.deviceType === 'Mobile' ? <FiSmartphone className="text-primary" /> : <FiMonitor className="text-primary" />}
            <div className="flex-grow-1 min-w-0"><div className="fw-medium truncate">{d.name}</div><div className="small text-muted">Last seen {relative(d.lastSeenAt)} · {d.lastIpAddress}</div></div>
            <Button size="sm" variant="link" className="text-danger" onClick={async () => { if (await confirm({ title: 'Remove this device?', body: 'It will be signed out immediately.', confirmText: 'Remove' })) void act(() => api.delete(`/api/auth/devices/${d.id}`), 'Device removed') }}><FiTrash2 /></Button>
          </div>))}</Card.Body>
      </Card></div>
      <div className="col-12"><Card>
        <Card.Header>Login history</Card.Header>
        <Card.Body className="p-0">
          {history.error ? <ErrorBox error={history.error} /> : <Table responsive hover className="mb-0 small">
            <thead><tr><th>Time</th><th>Event</th><th>Result</th><th>IP</th><th>Browser / OS</th></tr></thead>
            <tbody>{history.data?.items.map((h) => <tr key={h.id}><td>{dateTime(h.timestamp)}</td><td>{h.event.replace(/_/g, ' ')}</td>
              <td>{h.success ? <Badge bg="" className="bg-soft-success">OK</Badge> : <Badge bg="" className="bg-soft-danger">{h.failureReason?.replace(/_/g, ' ') ?? 'Failed'}</Badge>}</td>
              <td>{h.ipAddress}</td><td>{h.browser} {h.operatingSystem && `· ${h.operatingSystem}`}</td></tr>)}</tbody></Table>}
        </Card.Body>
        <Card.Footer><Pager page={page} totalPages={history.data?.totalPages ?? 1} onChange={setPage} /></Card.Footer>
      </Card></div>
    </div>
  )
}

type Prefs = Record<string, boolean>
function NotificationSettings() {
  const toast = useToast()
  const { data, setData, loading } = useAsync(() => api.get<Prefs>('/api/me/notification-preferences').then((r) => r.data), [])
  const [pushMsg, setPushMsg] = useState('')
  if (loading || !data) return <Loading />
  const save = async (next: Prefs) => { setData(next); try { await api.put('/api/me/notification-preferences', next) } catch (e) { toast.error(e) } }
  const row = (k: string, label: string, hint?: string) => (
    <div className="list-row" key={k}><div className="flex-grow-1"><div className="fw-medium">{label}</div>{hint && <div className="small text-muted">{hint}</div>}</div>
      <Form.Check type="switch" id={k} checked={!!data[k]} onChange={(e) => void save({ ...data, [k]: e.target.checked })} aria-label={label} /></div>
  )
  return (
    <div className="row g-3">
      <div className="col-lg-6"><Card><Card.Header>Channels</Card.Header><Card.Body>
        {row('emailEnabled', 'Email notifications')}{row('smsEnabled', 'SMS notifications')}{row('pushEnabled', 'Push notifications')}
        <div className="mt-3"><Button size="sm" variant="outline-primary" onClick={async () => { try { const r = await enablePush(); setPushMsg(r === 'ok' ? 'Push notifications enabled on this device.' : r) } catch (e) { setPushMsg(apiError(e).message) } }}><FiBell /> Enable push on this device</Button>
          {pushMsg && <div className="small mt-2 text-muted">{pushMsg}</div>}</div>
        <Alert variant="light" className="small mt-3 mb-0 border">Security messages (OTP, password reset, sign-in alerts for important changes) are always delivered for your protection.</Alert>
      </Card.Body></Card></div>
      <div className="col-lg-6"><Card><Card.Header>What to notify me about</Card.Header><Card.Body>
        {row('meetingReminders', 'Meeting reminders')}{row('expenseReminders', 'Bill & budget reminders')}{row('udharReminders', 'Udhar reminders')}{row('generalReminders', 'Other reminders')}
        {row('friendRequests', 'Friend requests')}{row('chatMessages', 'New chat messages')}{row('loginAlerts', 'New sign-in alerts')}{row('marketing', 'News & offers', 'Occasional product updates')}
      </Card.Body></Card></div>
    </div>
  )
}

function DataSettings() {
  const toast = useToast()
  const reauth = useReauth()
  const { logout } = useAuth()
  const [show, setShow] = useState(false)
  const [pw, setPw] = useState('')
  const [reason, setReason] = useState('')
  const exportData = async () => {
    try { await withReauth(reauth, () => downloadFile('/api/me/export', 'nijmitra-export.zip')) } catch (e) { toast.error(e) }
  }
  const del = async () => {
    try {
      const r = await withReauth(reauth, () => api.post('/api/me/delete-account', { password: pw, reason }))
      if (r) { toast.success(`Account scheduled for deletion on ${new Date(r.data.scheduledFor).toLocaleDateString()}`); await logout() }
    } catch (e) { toast.error(e) }
  }
  return (
    <div className="row g-3">
      <div className="col-lg-6"><Card className="h-100"><Card.Body>
        <h6 className="fw-bold"><FiDownload className="me-2" />Export my data</h6>
        <p className="small text-muted">Download a ZIP of all your records and files. Password-vault items stay encrypted with your master password.</p>
        <Button onClick={() => void exportData()}>Download my data</Button>
      </Card.Body></Card></div>
      <div className="col-lg-6"><Card className="h-100 border-danger-subtle"><Card.Body>
        <h6 className="fw-bold text-danger"><FiTrash2 className="me-2" />Delete account</h6>
        <p className="small text-muted">Your account is deactivated immediately and permanently deleted after the grace period. Contact support within that time to restore it.</p>
        <Button variant="outline-danger" onClick={() => setShow(true)}>Request account deletion</Button>
      </Card.Body></Card></div>
      <Modal show={show} onHide={() => setShow(false)} centered>
        <Modal.Header closeButton><Modal.Title className="h5">Delete your account?</Modal.Title></Modal.Header>
        <Modal.Body>
          <Alert variant="danger" className="small">All your finance records, documents, vault, diary, contacts and chats will be permanently erased after the grace period. Export your data first if you need it.</Alert>
          <Form.Control type="password" className="mb-2" placeholder="Confirm your password" value={pw} onChange={(e) => setPw(e.target.value)} />
          <Form.Control as="textarea" rows={2} placeholder="Reason (optional)" value={reason} onChange={(e) => setReason(e.target.value)} />
        </Modal.Body>
        <Modal.Footer><Button variant="outline-secondary" onClick={() => setShow(false)}>Cancel</Button><Button variant="danger" disabled={!pw} onClick={() => void del()}>Delete my account</Button></Modal.Footer>
      </Modal>
    </div>
  )
}
