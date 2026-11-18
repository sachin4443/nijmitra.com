import { useEffect, useState } from 'react'
import { Alert, Badge, Button, Card, Form, Modal, Nav, Spinner, Table } from 'react-bootstrap'
import { useNavigate, useParams } from 'react-router-dom'
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { FiActivity, FiAlertTriangle, FiArrowDown, FiArrowLeft, FiArrowUp, FiCheckCircle, FiDatabase, FiHardDrive, FiKey, FiLock, FiLogIn, FiMail, FiPlus, FiSend, FiShield, FiSmartphone, FiTrash2, FiUnlock, FiUserCheck, FiUserX, FiUsers, FiX } from 'react-icons/fi'
import { api, setAccessToken, uploadFile } from '../../lib/api'
import { useAsync, useDebounced } from '../../lib/hooks'
import { bytes, date, dateTime, num, relative } from '../../lib/format'
import { useAuth } from '../../contexts/AuthContext'
import { useToast } from '../../contexts/ToastContext'
import { ErrorBox, Loading, PageHeader, Pager, StatCard, StatusBadge, useConfirm } from '../../components/ui'
import type { FooterLinkInfo, WebsiteInfo } from '../../lib/i18n'

interface Dash { totalUsers: number; activeUsers: number; inactiveUsers: number; suspendedUsers: number; pendingDeletion: number; newRegistrations7d: number; newRegistrations30d: number; twoFactorUsers: number; onlineNow: number
  loginActivity: { label: string; success: number; failed: number }[]; storageBytes: number; storedFiles: number; notificationsToday: number; delivery: { channel: string; sent: number; failed: number; pending: number }[]; queueBacklog: number; deadLetters: number; databaseHealthy: boolean; serverTimeUtc: string; openReports: number }

export function AdminDashboard() {
  const nav = useNavigate()
  const d = useAsync(() => api.get<Dash>('/api/admin/dashboard').then((r) => r.data), [])
  if (d.loading) return <Loading />
  if (d.error) return <ErrorBox error={d.error} onRetry={d.reload} />
  const x = d.data!
  return (
    <>
      <PageHeader title="Admin Dashboard" subtitle={`Platform overview · server time ${x.serverTimeUtc}`} icon={<FiActivity />} />
      <div className="row g-3 mb-3">
        <div className="col-6 col-xl-3"><StatCard label="Total users" value={num(x.totalUsers)} icon={<FiUsers />} onClick={() => nav('/admin/users')} /></div>
        <div className="col-6 col-xl-3"><StatCard label="Active (30 days)" value={num(x.activeUsers)} icon={<FiUserCheck />} tone="success" hint={`${x.onlineNow} online now`} /></div>
        <div className="col-6 col-xl-3"><StatCard label="New registrations" value={num(x.newRegistrations7d)} icon={<FiUsers />} tone="info" hint={`${x.newRegistrations30d} in 30 days`} /></div>
        <div className="col-6 col-xl-3"><StatCard label="Suspended / inactive" value={`${x.suspendedUsers} / ${x.inactiveUsers}`} icon={<FiUserX />} tone="danger" hint={`${x.pendingDeletion} pending deletion`} /></div>
        <div className="col-6 col-xl-3"><StatCard label="Two-step enabled" value={`${x.totalUsers ? Math.round((x.twoFactorUsers / x.totalUsers) * 100) : 0}%`} icon={<FiShield />} tone="success" hint={`${x.twoFactorUsers} users`} /></div>
        <div className="col-6 col-xl-3"><StatCard label="Storage used" value={bytes(x.storageBytes)} icon={<FiHardDrive />} tone="warning" hint={`${num(x.storedFiles)} files`} /></div>
        <div className="col-6 col-xl-3"><StatCard label="Notifications today" value={num(x.notificationsToday)} icon={<FiSend />} tone="info" hint={`Queue ${x.queueBacklog} · failed ${x.deadLetters}`} onClick={() => nav('/admin/communication/logs')} /></div>
        <div className="col-6 col-xl-3"><StatCard label="System health" value={x.databaseHealthy ? 'Healthy' : 'Database error'} icon={x.databaseHealthy ? <FiCheckCircle /> : <FiAlertTriangle />} tone={x.databaseHealthy ? 'success' : 'danger'} hint={`${x.openReports} open user reports`} onClick={() => nav('/admin/reports')} /></div>
      </div>
      <div className="row g-3">
        <div className="col-lg-7"><Card className="h-100"><Card.Header>Login activity (14 days)</Card.Header><Card.Body style={{ height: 300 }}>
          <ResponsiveContainer><BarChart data={x.loginActivity}><CartesianGrid strokeDasharray="3 3" stroke="var(--nm-border)" /><XAxis dataKey="label" stroke="var(--nm-muted)" /><YAxis allowDecimals={false} stroke="var(--nm-muted)" /><Tooltip /><Legend />
            <Bar dataKey="success" name="Successful" fill="#3AAA35" radius={[4, 4, 0, 0]} /><Bar dataKey="failed" name="Failed" fill="#E53935" radius={[4, 4, 0, 0]} /></BarChart></ResponsiveContainer>
        </Card.Body></Card></div>
        <div className="col-lg-5"><Card className="h-100"><Card.Header>Delivery statistics (30 days)</Card.Header><Card.Body className="p-0">
          <Table className="mb-0"><thead><tr><th>Channel</th><th className="text-end">Sent</th><th className="text-end">Failed</th><th className="text-end">Queued</th></tr></thead>
            <tbody>{x.delivery.map((c) => <tr key={c.channel}><td>{c.channel === 'Email' ? <FiMail /> : c.channel === 'Sms' ? <FiSmartphone /> : <FiSend />} {c.channel}</td><td className="text-end text-success">{c.sent}</td><td className="text-end text-danger">{c.failed}</td><td className="text-end">{c.pending}</td></tr>)}</tbody></Table>
          <div className="p-3 small text-muted">Configure providers in Communication Center. Without an active provider a channel cannot deliver.</div>
        </Card.Body></Card></div>
      </div>
    </>
  )
}

interface UserRow { id: string; publicId: string; fullName: string; mobileNumber: string; email?: string; status: string; twoFactorEnabled: boolean; isLocked: boolean; roles: string[]; createdAt: string; lastLoginAt?: string }

export function AdminUsers() {
  const { id } = useParams()
  return id ? <AdminUserDetail id={id} /> : <AdminUserList />
}

function AdminUserList() {
  const nav = useNavigate()
  const [q, setQ] = useState('')
  const search = useDebounced(q)
  const [status, setStatus] = useState('')
  const [role, setRole] = useState('')
  const [tf, setTf] = useState('')
  const [page, setPage] = useState(1)
  const list = useAsync(() => api.get('/api/admin/users', { params: { search: search || undefined, status: status || undefined, role: role || undefined, twoFactor: tf || undefined, page, pageSize: 25 } }).then((r) => r.data as { items: UserRow[]; totalPages: number; totalCount: number }), [search, status, role, tf, page])
  return (
    <>
      <PageHeader title="User Management" subtitle="Click a user's name to view details, manage access, or sign in as them" icon={<FiUsers />} />
      <Card className="mb-3"><Card.Body className="row g-2">
        <div className="col-md-4"><Form.Control placeholder="Search name, mobile, email or NM ID…" value={q} onChange={(e) => { setQ(e.target.value); setPage(1) }} /></div>
        <div className="col-6 col-md-2"><Form.Select value={status} onChange={(e) => setStatus(e.target.value)}><option value="">Any status</option>{['Active', 'Inactive', 'Suspended', 'PendingDeletion'].map((s) => <option key={s}>{s}</option>)}</Form.Select></div>
        <div className="col-6 col-md-2"><Form.Select value={role} onChange={(e) => setRole(e.target.value)}><option value="">Any role</option><option value="none">Users only</option><option>SuperAdmin</option><option>Admin</option><option>SupportAdmin</option></Form.Select></div>
        <div className="col-6 col-md-2"><Form.Select value={tf} onChange={(e) => setTf(e.target.value)}><option value="">2FA: any</option><option value="true">2FA on</option><option value="false">2FA off</option></Form.Select></div>
      </Card.Body></Card>
      <Card>
        {list.loading && !list.data ? <Loading /> : <Table responsive hover className="mb-0 align-middle small">
          <thead><tr><th>User</th><th>NM ID</th><th>Mobile</th><th>Status</th><th>Security</th><th>Joined</th><th>Last login</th></tr></thead>
          <tbody>{list.data?.items.map((u) => (
            <tr key={u.id} className="clickable" onClick={() => nav(`/admin/users/${u.id}`)}>
              <td><a href="#" className="fw-semibold" onClick={(e) => e.preventDefault()}>{u.fullName}</a> {u.roles.map((r) => <Badge key={r} bg="" className="bg-soft-warning ms-1">{r}</Badge>)}<div className="text-muted">{u.email}</div></td>
              <td><code>{u.publicId}</code></td><td>{u.mobileNumber}</td><td><StatusBadge status={u.status} /> {u.isLocked && <Badge bg="danger"><FiLock /> Locked</Badge>}</td>
              <td>{u.twoFactorEnabled ? <Badge bg="" className="bg-soft-success">2FA</Badge> : <Badge bg="" className="badge-soft">Password</Badge>}</td>
              <td>{date(u.createdAt)}</td><td>{u.lastLoginAt ? relative(u.lastLoginAt) : 'Never'}</td>
            </tr>))}</tbody></Table>}
        <Card.Footer className="d-flex justify-content-between"><small className="text-muted">{list.data?.totalCount ?? 0} user(s)</small><Pager page={page} totalPages={list.data?.totalPages ?? 1} onChange={setPage} /></Card.Footer>
      </Card>
    </>
  )
}

interface Detail { user: UserRow; city?: string; state?: string; country?: string; mobileVerified: boolean; emailVerified: boolean; statusReason?: string; failedLoginCount: number; lockoutEndUtc?: string; passwordChangedAt?: string; activeSessions: number; devices: number; storageBytes: number; fileCount: number; recordCounts: Record<string, number> }

function AdminUserDetail({ id }: { id: string }) {
  const nav = useNavigate()
  const toast = useToast()
  const confirm = useConfirm()
  const { can, completeLogin, me } = useAuth()
  const d = useAsync(() => api.get<Detail>(`/api/admin/users/${id}`).then((r) => r.data), [id])
  const [hp, setHp] = useState(1)
  const history = useAsync(() => api.get(`/api/admin/users/${id}/login-history`, { params: { page: hp, pageSize: 10 } }).then((r) => r.data as { items: { id: string; event: string; success: boolean; failureReason?: string; ipAddress?: string; browser?: string; operatingSystem?: string; timestamp: string }[]; totalPages: number }), [id, hp])
  const [statusModal, setStatusModal] = useState<string | null>(null)
  const [reason, setReason] = useState('')
  const [temp, setTemp] = useState<string | null>(null)
  const [roles, setRoles] = useState<string[] | null>(null)
  const run = async (fn: () => Promise<unknown>, msg: string) => { try { await fn(); toast.success(msg); void d.reload() } catch (e) { toast.error(e) } }
  if (d.loading) return <Loading />
  if (!d.data) return <ErrorBox error={d.error} />
  const u = d.data.user
  const impersonate = async () => {
    if (!(await confirm({ title: `Sign in as ${u.fullName}?`, body: <>This action is <strong>audited</strong> and the user will be notified. Their password vault, chats and security settings stay locked. Use “Return to admin” in the red banner to come back.</>, confirmText: 'Sign in as user', variant: 'warning' }))) return
    try { const r = await api.post(`/api/auth/impersonate/${id}`); setAccessToken(r.data.accessToken); await completeLogin(r.data.accessToken); nav('/') } catch (e) { toast.error(e) }
  }
  return (
    <>
      <PageHeader title={u.fullName} subtitle={`${u.publicId} · ${u.mobileNumber}${u.email ? ' · ' + u.email : ''}`}>
        <Button size="sm" variant="outline-secondary" onClick={() => nav('/admin/users')}><FiArrowLeft /> Users</Button>
        {can('admin.users.impersonate') && u.roles.length === 0 && u.status === 'Active' && <Button size="sm" variant="warning" onClick={() => void impersonate()}><FiLogIn /> Login as user</Button>}
      </PageHeader>
      <div className="row g-3">
        <div className="col-lg-4">
          <Card className="mb-3"><Card.Header>Account</Card.Header><Card.Body className="small">
            <div className="list-row"><span className="flex-grow-1">Status</span><StatusBadge status={u.status} /></div>
            {d.data.statusReason && <div className="list-row text-muted">Reason: {d.data.statusReason}</div>}
            <div className="list-row"><span className="flex-grow-1">Roles</span>{u.roles.length ? u.roles.join(', ') : 'User'}</div>
            <div className="list-row"><span className="flex-grow-1">Joined</span>{dateTime(u.createdAt)}</div>
            <div className="list-row"><span className="flex-grow-1">Last login</span>{u.lastLoginAt ? dateTime(u.lastLoginAt) : 'Never'}</div>
            <div className="list-row"><span className="flex-grow-1">Location</span>{[d.data.city, d.data.state, d.data.country].filter(Boolean).join(', ') || '—'}</div>
            <div className="list-row"><span className="flex-grow-1">Storage</span>{bytes(d.data.storageBytes)} · {d.data.fileCount} files</div>
          </Card.Body></Card>
          <Card><Card.Header>Security status</Card.Header><Card.Body className="small">
            <div className="list-row"><span className="flex-grow-1">Two-step verification</span>{u.twoFactorEnabled ? <Badge bg="" className="bg-soft-success">Enabled</Badge> : <Badge bg="" className="bg-soft-warning">Off</Badge>}</div>
            <div className="list-row"><span className="flex-grow-1">Mobile verified</span>{d.data.mobileVerified ? 'Yes' : 'No'}</div>
            <div className="list-row"><span className="flex-grow-1">Email verified</span>{d.data.emailVerified ? 'Yes' : 'No'}</div>
            <div className="list-row"><span className="flex-grow-1">Failed logins</span>{d.data.failedLoginCount} {u.isLocked && <Badge bg="danger">Locked until {dateTime(d.data.lockoutEndUtc)}</Badge>}</div>
            <div className="list-row"><span className="flex-grow-1">Password changed</span>{dateTime(d.data.passwordChangedAt)}</div>
            <div className="list-row"><span className="flex-grow-1">Active sessions / devices</span>{d.data.activeSessions} / {d.data.devices}</div>
          </Card.Body></Card>
        </div>
        <div className="col-lg-8">
          {can('admin.users.manage') && <Card className="mb-3"><Card.Header>Actions</Card.Header><Card.Body className="d-flex flex-wrap gap-2">
            {u.status !== 'Active' && <Button size="sm" variant="success" onClick={() => void run(() => api.post(`/api/admin/users/${id}/status`, { status: 'Active', notifyUser: true }), 'User activated')}><FiUserCheck /> Activate</Button>}
            {u.status === 'Active' && <Button size="sm" variant="outline-warning" onClick={() => setStatusModal('Inactive')}>Deactivate</Button>}
            {u.status !== 'Suspended' && <Button size="sm" variant="outline-danger" onClick={() => setStatusModal('Suspended')}><FiUserX /> Suspend</Button>}
            {u.isLocked && <Button size="sm" variant="outline-primary" onClick={() => void run(() => api.post(`/api/admin/users/${id}/unlock`), 'Account unlocked')}><FiUnlock /> Unlock</Button>}
            <Button size="sm" variant="outline-primary" onClick={async () => { if (await confirm({ title: 'Issue a temporary password?', body: 'All sessions end and the user must choose a new password at next login.', confirmText: 'Reset password', variant: 'primary' })) try { setTemp((await api.post(`/api/admin/users/${id}/reset-password`)).data.temporaryPassword) } catch (e) { toast.error(e) } }}><FiKey /> Reset password</Button>
            {u.twoFactorEnabled && <Button size="sm" variant="outline-primary" onClick={async () => { if (await confirm({ title: 'Reset two-step verification?', body: 'Use only after verifying the user\'s identity (lost phone).', confirmText: 'Reset 2FA', variant: 'primary' })) void run(() => api.post(`/api/admin/users/${id}/reset-2fa`), '2FA reset') }}><FiShield /> Reset 2FA</Button>}
            <Button size="sm" variant="outline-secondary" onClick={() => void run(() => api.post(`/api/admin/users/${id}/revoke-sessions`), 'Sessions ended')}>End all sessions</Button>
            {can('admin.roles.manage') && <Button size="sm" variant="outline-secondary" onClick={() => setRoles(u.roles)}>Manage roles</Button>}
          </Card.Body></Card>}
          <Card className="mb-3"><Card.Header>Data overview <small className="text-muted fw-normal">(counts only — personal content is never visible to admins)</small></Card.Header><Card.Body>
            <div className="row g-2">{Object.entries(d.data.recordCounts).map(([k, v]) => <div key={k} className="col-6 col-md-4"><div className="border rounded p-2 small"><div className="text-muted">{k}</div><div className="fw-bold fs-5">{v}</div></div></div>)}</div>
          </Card.Body></Card>
          <Card><Card.Header>Login history</Card.Header>
            <Table responsive size="sm" className="mb-0 small"><thead><tr><th>Time</th><th>Event</th><th>Result</th><th>IP</th><th>Browser</th></tr></thead>
              <tbody>{history.data?.items.map((h) => <tr key={h.id}><td>{dateTime(h.timestamp)}</td><td>{h.event.replace(/_/g, ' ')}</td><td>{h.success ? <Badge bg="" className="bg-soft-success">OK</Badge> : <Badge bg="" className="bg-soft-danger">{h.failureReason?.replace(/_/g, ' ')}</Badge>}</td><td>{h.ipAddress}</td><td>{h.browser} {h.operatingSystem}</td></tr>)}</tbody></Table>
            <Card.Footer><Pager page={hp} totalPages={history.data?.totalPages ?? 1} onChange={setHp} /></Card.Footer></Card>
        </div>
      </div>
      <Modal show={!!statusModal} onHide={() => setStatusModal(null)} centered>
        <Modal.Header closeButton><Modal.Title className="h5">{statusModal === 'Suspended' ? 'Suspend' : 'Deactivate'} {u.fullName}</Modal.Title></Modal.Header>
        <Modal.Body><Form.Control as="textarea" rows={3} placeholder="Reason (shared with the user)" value={reason} onChange={(e) => setReason(e.target.value)} /></Modal.Body>
        <Modal.Footer><Button variant="outline-secondary" onClick={() => setStatusModal(null)}>Cancel</Button><Button variant="danger" onClick={() => { void run(() => api.post(`/api/admin/users/${id}/status`, { status: statusModal, reason, notifyUser: true }), 'Status updated'); setStatusModal(null); setReason('') }}>Confirm</Button></Modal.Footer>
      </Modal>
      <Modal show={!!temp} onHide={() => setTemp(null)} centered><Modal.Body className="p-4 text-center"><FiKey size={30} className="text-primary mb-2" /><h5>Temporary password</h5><div className="secret fs-4 my-3 user-select-all">{temp}</div><p className="small text-muted">Share it with the user through a trusted channel. It is shown only once.</p><Button onClick={() => setTemp(null)}>Done</Button></Modal.Body></Modal>
      <Modal show={!!roles} onHide={() => setRoles(null)} centered>
        <Modal.Header closeButton><Modal.Title className="h5">Roles for {u.fullName}</Modal.Title></Modal.Header>
        <Modal.Body>{['SuperAdmin', 'Admin', 'SupportAdmin'].map((r) => <Form.Check key={r} id={`r-${r}`} label={r} checked={roles?.includes(r) ?? false} disabled={u.id === me?.id && r === 'SuperAdmin'} onChange={(e) => setRoles(e.target.checked ? [...(roles ?? []), r] : (roles ?? []).filter((x) => x !== r))} />)}</Modal.Body>
        <Modal.Footer><Button onClick={() => { void run(() => api.put(`/api/admin/users/${id}/roles`, { roles }), 'Roles updated'); setRoles(null) }}>Save roles</Button></Modal.Footer>
      </Modal>
    </>
  )
}

export function AdminRoles() {
  const toast = useToast()
  const confirm = useConfirm()
  const roles = useAsync(() => api.get('/api/admin/roles').then((r) => r.data as { id: string; name: string; description?: string; isSystem: boolean; permissions: string[]; userCount: number }[]), [])
  const perms = useAsync(() => api.get('/api/admin/permissions').then((r) => r.data as { key: string; description?: string }[]), [])
  const [edit, setEdit] = useState<{ id?: string; name: string; description?: string; permissions: string[]; isSystem?: boolean } | null>(null)
  const save = async () => {
    if (!edit) return
    try { if (edit.id) await api.put(`/api/admin/roles/${edit.id}`, edit); else await api.post('/api/admin/roles', edit); toast.success('Role saved'); setEdit(null); void roles.reload() } catch (e) { toast.error(e) }
  }
  return (
    <>
      <PageHeader title="Roles & Permissions" subtitle="Granular role-based access control" icon={<FiShield />}><Button onClick={() => setEdit({ name: '', permissions: [] })}>New role</Button></PageHeader>
      <div className="row g-3">{roles.data?.map((r) => (
        <div key={r.id} className="col-md-6 col-xl-4"><Card className="h-100"><Card.Body>
          <div className="d-flex justify-content-between"><h6 className="fw-bold">{r.name} {r.isSystem && <Badge bg="" className="badge-soft">Built-in</Badge>}</h6><Badge bg="" className="bg-soft-info">{r.userCount} users</Badge></div>
          <div className="small text-muted mb-2">{r.description}</div>
          <div>{r.permissions.map((p) => <span key={p} className="tag">{p.replace('admin.', '')}</span>)}</div>
          <div className="mt-2 d-flex gap-2">{r.name !== 'SuperAdmin' && <Button size="sm" variant="outline-primary" onClick={() => setEdit({ ...r })}>Edit permissions</Button>}
            {!r.isSystem && <Button size="sm" variant="outline-danger" onClick={async () => { if (await confirm({ title: `Delete role ${r.name}?` })) { await api.delete(`/api/admin/roles/${r.id}`); void roles.reload() } }}>Delete</Button>}</div>
        </Card.Body></Card></div>))}</div>
      <Modal show={!!edit} onHide={() => setEdit(null)} centered size="lg">
        <Modal.Header closeButton><Modal.Title className="h5">{edit?.id ? `Edit ${edit.name}` : 'New role'}</Modal.Title></Modal.Header>
        <Modal.Body>
          <Form.Control className="mb-2" placeholder="Role name" value={edit?.name ?? ''} disabled={edit?.isSystem} onChange={(e) => setEdit(edit && { ...edit, name: e.target.value })} />
          <Form.Control className="mb-3" placeholder="Description" value={edit?.description ?? ''} onChange={(e) => setEdit(edit && { ...edit, description: e.target.value })} />
          {perms.data?.map((p) => <Form.Check key={p.key} id={p.key} label={<><code>{p.key}</code> <span className="small text-muted">{p.description}</span></>} checked={edit?.permissions.includes(p.key) ?? false}
            onChange={(e) => setEdit(edit && { ...edit, permissions: e.target.checked ? [...edit.permissions, p.key] : edit.permissions.filter((x) => x !== p.key) })} />)}
        </Modal.Body>
        <Modal.Footer><Button onClick={() => void save()}>Save</Button></Modal.Footer>
      </Modal>
    </>
  )
}

type Settings = Record<string, unknown> & { allowedExtensions: string[] }
export function AdminSettings() {
  const toast = useToast()
  const s = useAsync(() => api.get<Settings>('/api/admin/settings').then((r) => r.data), [])
  const [f, setF] = useState<Settings | null>(null)
  const [tab, setTab] = useState('general')
  useEffect(() => { if (s.data) setF(s.data) }, [s.data])
  if (!f) return <Loading />
  const save = async () => { try { setF((await api.put<Settings>('/api/admin/settings', f)).data); toast.success('Settings saved') } catch (e) { toast.error(e) } }
  const txt = (k: string, label: string, type = 'text', help?: string) => <Form.Group className="col-md-6 mb-3"><Form.Label>{label}</Form.Label><Form.Control type={type} value={String(f[k] ?? '')} onChange={(e) => setF({ ...f, [k]: type === 'number' ? Number(e.target.value) : e.target.value })} />{help && <Form.Text>{help}</Form.Text>}</Form.Group>
  const sw = (k: string, label: string, help?: string) => <div className="col-12 mb-3"><Form.Check type="switch" id={k} label={<span className="fw-medium">{label}</span>} checked={!!f[k]} onChange={(e) => setF({ ...f, [k]: e.target.checked })} />{help && <div className="small text-muted ms-5">{help}</div>}</div>
  const brand = async (kind: string, file?: File) => { if (!file) return; try { setF(await uploadFile<Settings>(`/api/admin/branding/${kind}`, file)); toast.success('Branding updated') } catch (e) { toast.error(e) } }
  return (
    <>
      <PageHeader title="Platform Settings" subtitle="Application, security, registration, files and maintenance"><Button onClick={() => void save()}>Save settings</Button></PageHeader>
      <Nav variant="pills" activeKey={tab} onSelect={(k) => setTab(k ?? 'general')} className="mb-3">{[['general', 'General & branding'], ['website', 'Website & footer'], ['security', 'Sessions & security'], ['registration', 'Registration'], ['files', 'Files'], ['ops', 'Maintenance']].map(([k, l]) => <Nav.Item key={k}><Nav.Link eventKey={k}>{l}</Nav.Link></Nav.Item>)}</Nav>
      <Card><Card.Body><div className="row">
        {tab === 'general' && <>{txt('applicationName', 'Application name')}{txt('tagline', 'Tagline')}{txt('hindiTagline', 'Hindi tagline')}{txt('supportEmail', 'Support email', 'email')}
          <div className="col-md-6 mb-3"><Form.Label>Custom logo</Form.Label><Form.Control type="file" accept="image/png,image/jpeg,image/webp" onChange={(e) => void brand('logo', (e.target as HTMLInputElement).files?.[0])} /><Form.Text>{f.logoFileId ? 'Custom logo uploaded. ' : 'Using the default NijMitra logo. '}</Form.Text>{!!f.logoFileId && <Button size="sm" variant="link" onClick={() => void api.delete('/api/admin/branding/logo').then((r) => setF(r.data))}>Reset</Button>}</div>
          <div className="col-md-6 mb-3"><Form.Label>Custom favicon</Form.Label><Form.Control type="file" accept=".png,.ico" onChange={(e) => void brand('favicon', (e.target as HTMLInputElement).files?.[0])} />{!!f.faviconFileId && <Button size="sm" variant="link" onClick={() => void api.delete('/api/admin/branding/favicon').then((r) => setF(r.data))}>Reset</Button>}</div></>}
        {tab === 'website' && <WebsiteEditor value={f.website as WebsiteInfo} onChange={(w) => setF({ ...f, website: w })} />}
        {tab === 'security' && <>{txt('sessionIdleTimeoutSeconds', 'Auto-logout after inactivity (seconds)', 'number', 'Default 60. Enforced in the browser and on the server.')}{txt('sessionWarningSeconds', 'Warning before logout (seconds)', 'number')}
          {txt('sessionAbsoluteHours', 'Maximum session length (hours)', 'number')}{txt('maxLoginAttempts', 'Failed logins before lockout', 'number')}{txt('lockoutMinutes', 'Lockout duration (minutes)', 'number')}
          {sw('enforceTwoFactor', 'Require two-step verification for everyone', 'When on, users without an authenticator must set one up at their next login.')}
          {sw('notifyUserOnAdminAccess', 'Notify users when an admin signs in as them')}
          {txt('accountDeletionGraceDays', 'Account deletion grace period (days)', 'number')}</>}
        {tab === 'registration' && <>{sw('allowRegistration', 'Allow new registrations')}
          {sw('requireMobileOtp', 'Require mobile OTP at registration', 'Can be enabled only after an SMS provider is configured and active in Communication Center.')}
          {sw('requireEmailVerification', 'Require verified email for email notifications', 'Can be enabled only after an email provider is configured and active.')}</>}
        {tab === 'files' && <>{txt('maxUploadMb', 'Maximum upload size (MB)', 'number')}{txt('userStorageQuotaMb', 'Storage quota per user (MB)', 'number')}
          <Form.Group className="col-12 mb-3"><Form.Label>Allowed file extensions</Form.Label><Form.Control value={f.allowedExtensions.join(', ')} onChange={(e) => setF({ ...f, allowedExtensions: e.target.value.split(',').map((x) => x.trim()).filter(Boolean) })} /><Form.Text>Only known safe types are accepted (pdf, doc, docx, xls, xlsx, csv, jpg, png, webp, txt, mp3, m4a, ogg, wav, webm, mp4, mov).</Form.Text></Form.Group></>}
        {tab === 'ops' && <>{sw('maintenanceMode', 'Maintenance mode', 'Users see the message below; administrators can still sign in.')}<Form.Group className="col-12 mb-3"><Form.Label>Maintenance message</Form.Label><Form.Control as="textarea" rows={2} value={String(f.maintenanceMessage ?? '')} onChange={(e) => setF({ ...f, maintenanceMessage: e.target.value })} /></Form.Group>
          {sw('chatEnabled', 'Friends & chat enabled')}</>}
      </div></Card.Body></Card>
    </>
  )
}

export function AdminDatabase() {
  const toast = useToast()
  const info = useAsync(() => api.get('/api/admin/database').then((r) => r.data as { source: string; host?: string; port?: number; database?: string; username?: string; hasPassword: boolean; sslRequired: boolean; updatedAt?: string }), [])
  const [f, setF] = useState({ host: '', port: 5432, database: '', username: '', password: '', ssl: false, applyMigrations: true })
  const [result, setResult] = useState<{ success: boolean; message: string; serverVersion?: string } | null>(null)
  const [busy, setBusy] = useState('')
  useEffect(() => { if (info.data) setF((x) => ({ ...x, host: info.data!.host ?? '', port: info.data!.port ?? 5432, database: info.data!.database ?? '', username: info.data!.username ?? '', ssl: info.data!.sslRequired })) }, [info.data])
  const test = async () => { setBusy('test'); try { setResult((await api.post('/api/admin/database/test', f)).data) } catch (e) { toast.error(e) } finally { setBusy('') } }
  const save = async () => { setBusy('save'); try { const r = await api.put('/api/admin/database', f); toast.success(r.data.message); void info.reload() } catch (e) { toast.error(e) } finally { setBusy('') } }
  return (
    <>
      <PageHeader title="Database Connection" subtitle="PostgreSQL connection used by NijMitra" icon={<FiDatabase />} />
      <Alert variant="warning" className="small"><FiAlertTriangle /> Changing the database switches all users to the new database immediately. The connection is tested first; the password is stored encrypted on the server and is never shown again.</Alert>
      <div className="row g-3">
        <div className="col-lg-5"><Card><Card.Header>Current connection</Card.Header><Card.Body className="small">
          {info.data && <><div className="list-row"><span className="flex-grow-1">Configured from</span><Badge bg="" className="badge-soft">{info.data.source === 'admin-panel' ? 'Admin panel' : 'Server configuration'}</Badge></div>
            <div className="list-row"><span className="flex-grow-1">Host</span>{info.data.host}:{info.data.port}</div><div className="list-row"><span className="flex-grow-1">Database</span>{info.data.database}</div>
            <div className="list-row"><span className="flex-grow-1">Username</span>{info.data.username}</div><div className="list-row"><span className="flex-grow-1">Password</span>{info.data.hasPassword ? '•••••••• (set)' : 'not set'}</div>
            <div className="list-row"><span className="flex-grow-1">SSL</span>{info.data.sslRequired ? 'Required' : 'Preferred'}</div>{info.data.updatedAt && <div className="list-row"><span className="flex-grow-1">Last changed</span>{dateTime(info.data.updatedAt)}</div>}</>}
        </Card.Body></Card></div>
        <div className="col-lg-7"><Card><Card.Header>Update connection</Card.Header><Card.Body className="row g-3">
          <Form.Group className="col-md-8"><Form.Label>Host</Form.Label><Form.Control value={f.host} onChange={(e) => setF({ ...f, host: e.target.value })} /></Form.Group>
          <Form.Group className="col-md-4"><Form.Label>Port</Form.Label><Form.Control type="number" value={f.port} onChange={(e) => setF({ ...f, port: Number(e.target.value) })} /></Form.Group>
          <Form.Group className="col-md-6"><Form.Label>Database name</Form.Label><Form.Control value={f.database} onChange={(e) => setF({ ...f, database: e.target.value })} /></Form.Group>
          <Form.Group className="col-md-6"><Form.Label>Username</Form.Label><Form.Control value={f.username} onChange={(e) => setF({ ...f, username: e.target.value })} /></Form.Group>
          <Form.Group className="col-md-8"><Form.Label>Password</Form.Label><Form.Control type="password" autoComplete="new-password" placeholder="Leave blank to keep the current password" value={f.password} onChange={(e) => setF({ ...f, password: e.target.value })} /></Form.Group>
          <Form.Check className="col-md-4 align-self-end mb-2" type="switch" id="ssl" label="Require SSL" checked={f.ssl} onChange={(e) => setF({ ...f, ssl: e.target.checked })} />
          <Form.Check className="col-12" type="switch" id="mig" label="Create / update tables automatically (run migrations)" checked={f.applyMigrations} onChange={(e) => setF({ ...f, applyMigrations: e.target.checked })} />
          {result && <div className="col-12"><Alert variant={result.success ? 'success' : 'danger'} className="small mb-0">{result.message}{result.serverVersion && <div className="text-muted">{result.serverVersion}</div>}</Alert></div>}
          <div className="col-12 d-flex gap-2"><Button variant="outline-primary" disabled={!!busy} onClick={() => void test()}>{busy === 'test' ? <Spinner size="sm" /> : 'Test connection'}</Button>
            <Button disabled={!!busy || !result?.success} onClick={() => void save()}>{busy === 'save' ? <Spinner size="sm" /> : 'Save & switch'}</Button></div>
        </Card.Body></Card></div>
      </div>
    </>
  )
}

export function AdminAudit() {
  const [kind, setKind] = useState('activity')
  const [event, setEvent] = useState('')
  const [success, setSuccess] = useState('')
  const [page, setPage] = useState(1)
  const ev = useDebounced(event)
  const list = useAsync(() => api.get('/api/admin/audit', { params: { kind, event: ev || undefined, success: success || undefined, page, pageSize: 50 } }).then((r) => r.data as { items: { id: string; timestamp: string; userName?: string; event: string; entityType?: string; details?: string; success: boolean; ipAddress?: string; browser?: string; operatingSystem?: string; device?: string }[]; totalPages: number }), [kind, ev, success, page])
  return (
    <>
      <PageHeader title="Audit Logs" subtitle="Security and activity trail — secrets are never recorded" icon={<FiShield />} />
      <Card className="mb-3"><Card.Body className="d-flex flex-wrap gap-2">
        <Nav variant="pills" activeKey={kind} onSelect={(k) => { setKind(k ?? 'activity'); setPage(1) }}><Nav.Item><Nav.Link eventKey="activity">Activity</Nav.Link></Nav.Item><Nav.Item><Nav.Link eventKey="login">Logins</Nav.Link></Nav.Item><Nav.Item><Nav.Link eventKey="admin">Admin actions</Nav.Link></Nav.Item></Nav>
        <Form.Control size="sm" placeholder="Event (e.g. login_success, file_download)" value={event} onChange={(e) => setEvent(e.target.value)} style={{ maxWidth: 280 }} />
        <Form.Select size="sm" value={success} onChange={(e) => setSuccess(e.target.value)} style={{ maxWidth: 150 }}><option value="">Any result</option><option value="true">Success</option><option value="false">Failure</option></Form.Select>
      </Card.Body></Card>
      <Card>{list.loading && !list.data ? <Loading /> : <Table responsive hover size="sm" className="mb-0 small">
        <thead><tr><th>Time</th><th>User</th><th>Event</th><th>Details</th><th>Result</th><th>IP</th><th>Browser / OS / Device</th></tr></thead>
        <tbody>{list.data?.items.map((l) => <tr key={l.id}><td className="text-nowrap">{dateTime(l.timestamp)}</td><td>{l.userName ?? '—'}</td><td><code>{l.event}</code></td><td>{l.entityType && <Badge bg="" className="badge-soft me-1">{l.entityType}</Badge>}{l.details}</td>
          <td>{l.success ? <FiCheckCircle className="text-success" /> : <FiAlertTriangle className="text-danger" />}</td><td>{l.ipAddress}</td><td>{[l.browser, l.operatingSystem, l.device].filter(Boolean).join(' · ')}</td></tr>)}</tbody></Table>}
        <Card.Footer><Pager page={page} totalPages={list.data?.totalPages ?? 1} onChange={setPage} /></Card.Footer></Card>
    </>
  )
}

export function AdminReports() {
  const toast = useToast()
  const [status, setStatus] = useState('Open')
  const list = useAsync(() => api.get('/api/admin/reports', { params: { status: status || undefined } }).then((r) => r.data as { items: { id: string; reporterName: string; reportedName: string; reportedUserId: string; reason: string; details?: string; status: string; createdAt: string }[] }), [status])
  const nav = useNavigate()
  return (
    <>
      <PageHeader title="User Reports" subtitle="Reports submitted from chat" icon={<FiAlertTriangle />} />
      <Nav variant="pills" activeKey={status} onSelect={(k) => setStatus(k ?? '')} className="mb-3">{['Open', 'Reviewed', 'ActionTaken', 'Dismissed', ''].map((s) => <Nav.Item key={s}><Nav.Link eventKey={s}>{s || 'All'}</Nav.Link></Nav.Item>)}</Nav>
      <Card><Card.Body>{list.data?.items.length === 0 ? <div className="text-muted small">No reports.</div> : list.data?.items.map((r) => (
        <div key={r.id} className="list-row align-items-start"><div className="flex-grow-1"><div><strong>{r.reportedName}</strong> reported by {r.reporterName} · <Badge bg="" className="bg-soft-danger">{r.reason}</Badge> <StatusBadge status={r.status} /></div>
          {r.details && <div className="small">{r.details}</div>}<div className="small text-muted">{dateTime(r.createdAt)}</div></div>
          <Button size="sm" variant="outline-secondary" onClick={() => nav(`/admin/users/${r.reportedUserId}`)}>View user</Button>
          {['Reviewed', 'ActionTaken', 'Dismissed'].map((s) => <Button key={s} size="sm" variant="link" onClick={() => void api.post(`/api/admin/reports/${r.id}`, null, { params: { status: s } }).then(() => { toast.success('Updated'); void list.reload() })}>{s.replace('ActionTaken', 'Action taken')}</Button>)}
        </div>))}</Card.Body></Card>
    </>
  )
}


const SOCIAL = ['facebook', 'instagram', 'x', 'youtube', 'linkedin', 'whatsapp', 'telegram', 'website']

/** Admin editor for the public website footer (Settings → Website & footer). Saved with "Save settings". */
function WebsiteEditor({ value, onChange }: { value: WebsiteInfo; onChange: (w: WebsiteInfo) => void }) {
  const w = value
  const set = (patch: Partial<WebsiteInfo>) => onChange({ ...w, ...patch })
  const setCol = (i: number, patch: Partial<WebsiteInfo['columns'][number]>) => set({ columns: w.columns.map((c, j) => (j === i ? { ...c, ...patch } : c)) })
  const setLink = (i: number, k: number, patch: Partial<FooterLinkInfo>) => setCol(i, { links: w.columns[i].links.map((l, j) => (j === k ? { ...l, ...patch } : l)) })
  const move = <T,>(arr: T[], i: number, d: number) => { const n = [...arr]; const [x] = n.splice(i, 1); n.splice(Math.max(0, Math.min(n.length, i + d)), 0, x); return n }
  const field = (k: keyof WebsiteInfo, label: string, help?: string, area = false) => (
    <Form.Group className={area ? 'col-md-6 mb-3' : 'col-md-4 mb-3'}><Form.Label>{label}</Form.Label>
      {area ? <Form.Control as="textarea" rows={3} value={String(w[k] ?? '')} onChange={(e) => set({ [k]: e.target.value } as Partial<WebsiteInfo>)} />
        : <Form.Control value={String(w[k] ?? '')} onChange={(e) => set({ [k]: e.target.value } as Partial<WebsiteInfo>)} />}{help && <Form.Text>{help}</Form.Text>}</Form.Group>)
  return (
    <>
      <div className="col-12"><Alert variant="info" className="small">This is the footer shown at the bottom of the home page (nijmitra.com). Each text has an English version and an optional Hindi version shown when a visitor picks हिन्दी. Click <b>Save settings</b> to publish changes.</Alert></div>
      {field('aboutEn', 'About text (English)', undefined, true)}{field('aboutHi', 'About text (Hindi, optional)', undefined, true)}
      {field('copyrightName', 'Copyright name', 'Shown as © year name')}{field('companyLine', 'Company line (optional)', 'e.g. "A product by …". Leave empty to hide.')}{field('playStoreUrl', 'Google Play link (optional)', 'When set, the Android card on the home page links to the Play Store.')}
      {field('contactEmail', 'Contact email')}{field('contactPhone', 'Contact phone')}{field('address', 'Address')}
      <div className="col-12"><hr /><div className="d-flex justify-content-between align-items-center mb-2"><h6 className="mb-0">Footer columns &amp; links</h6>
        <Button size="sm" variant="outline-primary" disabled={w.columns.length >= 6} onClick={() => set({ columns: [...w.columns, { titleEn: 'New column', titleHi: '', links: [] }] })}><FiPlus /> Add column</Button></div></div>
      {w.columns.map((c, i) => (
        <div className="col-lg-6 mb-3" key={i}><Card className="h-100"><Card.Header className="d-flex gap-2 align-items-center">
          <Form.Control size="sm" placeholder="Title (English)" value={c.titleEn} onChange={(e) => setCol(i, { titleEn: e.target.value })} />
          <Form.Control size="sm" placeholder="Title (Hindi)" value={c.titleHi ?? ''} onChange={(e) => setCol(i, { titleHi: e.target.value })} />
          <Button size="sm" variant="link" className="p-1" disabled={i === 0} onClick={() => set({ columns: move(w.columns, i, -1) })} aria-label="Move left"><FiArrowUp /></Button>
          <Button size="sm" variant="link" className="p-1" disabled={i === w.columns.length - 1} onClick={() => set({ columns: move(w.columns, i, 1) })} aria-label="Move right"><FiArrowDown /></Button>
          <Button size="sm" variant="link" className="p-1 text-danger" onClick={() => set({ columns: w.columns.filter((_, j) => j !== i) })} aria-label="Remove column"><FiTrash2 /></Button>
        </Card.Header><Card.Body className="small">
          {c.links.map((l, k) => <div className="d-flex gap-1 mb-2 align-items-center" key={k}>
            <Form.Control size="sm" placeholder="Label (English)" value={l.labelEn} onChange={(e) => setLink(i, k, { labelEn: e.target.value })} />
            <Form.Control size="sm" placeholder="Label (Hindi)" value={l.labelHi ?? ''} onChange={(e) => setLink(i, k, { labelHi: e.target.value })} />
            <Form.Control size="sm" placeholder="https://… or /page" value={l.url} onChange={(e) => setLink(i, k, { url: e.target.value })} />
            <Button size="sm" variant="link" className="p-1" disabled={k === 0} onClick={() => setCol(i, { links: move(c.links, k, -1) })} aria-label="Move up"><FiArrowUp /></Button>
            <Button size="sm" variant="link" className="p-1 text-danger" onClick={() => setCol(i, { links: c.links.filter((_, j) => j !== k) })} aria-label="Remove link"><FiX /></Button>
          </div>)}
          <Button size="sm" variant="link" className="p-0" disabled={c.links.length >= 12} onClick={() => setCol(i, { links: [...c.links, { labelEn: '', labelHi: '', url: '' }] })}><FiPlus /> Add link</Button>
        </Card.Body></Card></div>))}
      <div className="col-12"><Form.Text>Links can be https://…, mailto:…, tel:…, a page on this site like /privacy.html, or a home-page section like #features.</Form.Text><hr /><h6>Social media</h6></div>
      {w.socialLinks.map((sl, i) => <div className="col-md-6 d-flex gap-2 mb-2" key={i}>
        <Form.Select size="sm" style={{ maxWidth: 150 }} value={sl.platform} onChange={(e) => set({ socialLinks: w.socialLinks.map((x, j) => (j === i ? { ...x, platform: e.target.value } : x)) })}>{SOCIAL.map((p) => <option key={p} value={p}>{p === 'x' ? 'X (Twitter)' : p[0].toUpperCase() + p.slice(1)}</option>)}</Form.Select>
        <Form.Control size="sm" placeholder="https://…" value={sl.url} onChange={(e) => set({ socialLinks: w.socialLinks.map((x, j) => (j === i ? { ...x, url: e.target.value } : x)) })} />
        <Button size="sm" variant="link" className="text-danger" onClick={() => set({ socialLinks: w.socialLinks.filter((_, j) => j !== i) })} aria-label="Remove"><FiX /></Button></div>)}
      <div className="col-12"><Button size="sm" variant="outline-primary" disabled={w.socialLinks.length >= 10} onClick={() => set({ socialLinks: [...w.socialLinks, { platform: 'instagram', url: '' }] })}><FiPlus /> Add social link</Button></div>
      <div className="col-12"><hr /><h6>Android app (Google Play)</h6>
        <p className="small text-muted mb-2">The NijMitra Android app opens this website full-screen only when the fingerprints below match the app's signing key. After the first upload to Google Play, copy the <b>SHA-256 certificate fingerprint</b> from Play Console → Test and release → App integrity → App signing and add it here (keep the upload-key fingerprint too). Published at <a href="/.well-known/assetlinks.json" target="_blank" rel="noreferrer">/.well-known/assetlinks.json</a>.</p></div>
      <Form.Group className="col-md-4 mb-3"><Form.Label>Android package name</Form.Label><Form.Control value={w.androidPackageName ?? ''} onChange={(e) => set({ androidPackageName: e.target.value })} /></Form.Group>
      <Form.Group className="col-md-8 mb-3"><Form.Label>SHA-256 certificate fingerprints (one per line)</Form.Label>
        <Form.Control as="textarea" rows={3} className="font-monospace small" value={(w.androidSha256Fingerprints ?? []).join('\n')} onChange={(e) => set({ androidSha256Fingerprints: e.target.value.split('\n') })} /></Form.Group>
    </>
  )
}
