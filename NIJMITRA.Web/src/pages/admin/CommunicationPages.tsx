import { useEffect, useMemo, useState } from 'react'
import { Alert, Badge, Button, Card, Form, Modal, Nav, Spinner, Table } from 'react-bootstrap'
import { NavLink, useParams } from 'react-router-dom'
import { FiBell, FiCheckCircle, FiEdit2, FiKey, FiMail, FiMessageSquare, FiPlus, FiRadio, FiSend, FiSmartphone, FiTrash2, FiXCircle, FiZap } from 'react-icons/fi'
import { api } from '../../lib/api'
import { useAsync } from '../../lib/hooks'
import { dateTime, relative } from '../../lib/format'
import { useToast } from '../../contexts/ToastContext'
import { EmptyState, Loading, PageHeader, Pager, StatusBadge, useConfirm } from '../../components/ui'

interface FieldDef { key: string; label: string; type: string; required: boolean; secret: boolean; default?: string; help?: string; options?: string[] }
interface ProviderDef { type: string; channel: string; displayName: string; description: string; fields: FieldDef[] }
interface Provider { id: string; channel: string; providerType: string; name: string; isActive: boolean; isDefault: boolean; priority: number; settings: Record<string, string | null>; secretsConfigured: string[]; lastTestedAt?: string; lastTestSucceeded?: boolean; lastTestMessage?: string }
const CH_ICON: Record<string, React.ReactNode> = { Email: <FiMail />, Sms: <FiSmartphone />, Push: <FiBell />, WhatsApp: <FiMessageSquare /> }

export default function CommunicationCenter() {
  const { section = 'providers' } = useParams()
  const tabs = [['providers', 'Providers'], ['policies', 'Notification control'], ['templates', 'Templates'], ['logs', 'Delivery logs'], ['announcements', 'Announcements']]
  return (
    <>
      <PageHeader title="Communication Center" subtitle="Email, SMS, push and WhatsApp — configure everything without code changes" icon={<FiRadio />} />
      <Nav variant="pills" className="mb-3 gap-1 flex-nowrap overflow-auto">{tabs.map(([k, l]) => <Nav.Item key={k}><Nav.Link as={NavLink} to={`/admin/communication/${k}`} className="text-nowrap">{l}</Nav.Link></Nav.Item>)}</Nav>
      {section === 'providers' && <Providers />}
      {section === 'policies' && <Policies />}
      {section === 'templates' && <Templates />}
      {section === 'logs' && <Logs />}
      {section === 'announcements' && <Announcements />}
    </>
  )
}

function Providers() {
  const toast = useToast()
  const confirm = useConfirm()
  const defs = useAsync(() => api.get<ProviderDef[]>('/api/admin/communication/provider-definitions').then((r) => r.data), [])
  const list = useAsync(() => api.get<Provider[]>('/api/admin/communication/providers').then((r) => r.data), [])
  const [edit, setEdit] = useState<{ def: ProviderDef; provider?: Provider } | null>(null)
  const [testing, setTesting] = useState<string | null>(null)
  const [sendTo, setSendTo] = useState<Provider | null>(null)
  if (defs.loading || list.loading) return <Loading />
  const channels = ['Email', 'Sms', 'Push', 'WhatsApp']
  const test = async (p: Provider) => { setTesting(p.id); try { const r = await api.post<Provider>(`/api/admin/communication/providers/${p.id}/test-connection`); toast.push(r.data.lastTestSucceeded ? 'success' : 'error', r.data.lastTestMessage ?? ''); void list.reload() } catch (e) { toast.error(e) } finally { setTesting(null) } }
  return (
    <>
      {channels.map((ch) => {
        const items = list.data?.filter((p) => p.channel === ch) ?? []
        const chDefs = defs.data?.filter((d) => d.channel === ch) ?? []
        return (
          <Card key={ch} className="mb-3">
            <Card.Header className="d-flex justify-content-between align-items-center"><span>{CH_ICON[ch]} {ch === 'Sms' ? 'SMS' : ch} providers {items.some((p) => p.isActive) ? <Badge bg="" className="bg-soft-success ms-2">Active</Badge> : <Badge bg="" className="bg-soft-warning ms-2">Not configured</Badge>}</span>
              <Form.Select size="sm" style={{ maxWidth: 240 }} value="" onChange={(e) => { const d = chDefs.find((x) => x.type === e.target.value); if (d) setEdit({ def: d }) }}><option value="">+ Add provider…</option>{chDefs.map((d) => <option key={d.type} value={d.type}>{d.displayName}</option>)}</Form.Select></Card.Header>
            <Card.Body>
              {ch === 'WhatsApp' && items.length === 0 && <Alert variant="light" className="border small mb-0">WhatsApp delivery is <strong>not active</strong>. It becomes operational only after you add a verified WhatsApp Business Cloud API number, access token and approved templates, and a test message succeeds.</Alert>}
              {items.length === 0 && ch !== 'WhatsApp' && <div className="small text-muted">No provider configured. {ch === 'Sms' ? 'Mobile OTP and SMS alerts stay disabled until one is active.' : ch === 'Email' ? 'Email verification and email alerts stay disabled until one is active.' : 'Generate VAPID keys to enable browser/PWA push.'}</div>}
              {items.map((p) => (
                <div key={p.id} className="list-row flex-wrap">
                  <div className="flex-grow-1 min-w-0"><div className="fw-semibold">{p.name} {p.isDefault && <Badge bg="primary">Default</Badge>} <span className="small text-muted">· {defs.data?.find((d) => d.type === p.providerType)?.displayName} · priority {p.priority}</span></div>
                    <div className="small text-muted">{p.lastTestedAt ? <>{p.lastTestSucceeded ? <FiCheckCircle className="text-success" /> : <FiXCircle className="text-danger" />} Tested {relative(p.lastTestedAt)}: {p.lastTestMessage}</> : 'Not tested yet'}</div></div>
                  <Form.Check type="switch" id={`act-${p.id}`} label={p.isActive ? 'Enabled' : 'Disabled'} checked={p.isActive} onChange={async (e) => { try { await api.post(`/api/admin/communication/providers/${p.id}/active`, null, { params: { value: e.target.checked } }); void list.reload() } catch (x) { toast.error(x) } }} />
                  <Button size="sm" variant="outline-secondary" disabled={testing === p.id} onClick={() => void test(p)}>{testing === p.id ? <Spinner size="sm" /> : <><FiZap /> Test connection</>}</Button>
                  <Button size="sm" variant="outline-primary" onClick={() => setSendTo(p)}><FiSend /> Send test</Button>
                  <Button size="sm" variant="link" onClick={() => setEdit({ def: defs.data!.find((d) => d.type === p.providerType)!, provider: p })}><FiEdit2 /></Button>
                  <Button size="sm" variant="link" className="text-danger" onClick={async () => { if (await confirm({ title: `Delete provider ${p.name}?` })) { await api.delete(`/api/admin/communication/providers/${p.id}`); void list.reload() } }}><FiTrash2 /></Button>
                </div>))}
            </Card.Body>
          </Card>)
      })}
      {edit && <ProviderModal def={edit.def} provider={edit.provider} onClose={(ok) => { setEdit(null); if (ok) void list.reload() }} />}
      {sendTo && <TestSendModal provider={sendTo} onClose={() => { setSendTo(null); void list.reload() }} />}
    </>
  )
}

function ProviderModal({ def, provider, onClose }: { def: ProviderDef; provider?: Provider; onClose: (ok: boolean) => void }) {
  const toast = useToast()
  const [name, setName] = useState(provider?.name ?? def.displayName)
  const [settings, setSettings] = useState<Record<string, string>>(() => Object.fromEntries(def.fields.filter((f) => !f.secret).map((f) => [f.key, provider?.settings[f.key] ?? f.default ?? ''])))
  const [secrets, setSecrets] = useState<Record<string, string>>({})
  const [active, setActive] = useState(provider?.isActive ?? true)
  const [isDefault, setIsDefault] = useState(provider?.isDefault ?? true)
  const [priority, setPriority] = useState(provider?.priority ?? 100)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const save = async () => {
    try {
      const body = { channel: def.channel, providerType: def.type, name, isActive: active, isDefault, priority, settings, secrets }
      if (provider) await api.put(`/api/admin/communication/providers/${provider.id}`, body); else await api.post('/api/admin/communication/providers', body)
      toast.success('Provider saved. Use “Test connection” to verify it.'); onClose(true)
    } catch (x) { const { apiError } = await import('../../lib/api'); const e = apiError(x); setErrors(e.fields); toast.push('error', e.message) }
  }
  const genVapid = async () => { const r = await api.post('/api/admin/communication/vapid-keys'); setSettings({ ...settings, publicKey: r.data.publicKey }); setSecrets({ ...secrets, privateKey: r.data.privateKey }); toast.success('New VAPID key pair generated') }
  return (
    <Modal show onHide={() => onClose(false)} size="lg" centered>
      <Modal.Header closeButton><Modal.Title className="h5">{provider ? 'Edit' : 'Add'} {def.displayName}</Modal.Title></Modal.Header>
      <Modal.Body>
        <p className="small text-muted">{def.description}</p>
        {def.type === 'WebPushVapid' && <Button size="sm" variant="outline-primary" className="mb-3" onClick={() => void genVapid()}><FiKey /> Generate VAPID keys</Button>}
        <div className="row g-3">
          <Form.Group className="col-md-6"><Form.Label>Display name</Form.Label><Form.Control value={name} onChange={(e) => setName(e.target.value)} /></Form.Group>
          <Form.Group className="col-md-3"><Form.Label>Priority</Form.Label><Form.Control type="number" min={1} value={priority} onChange={(e) => setPriority(Number(e.target.value))} /><Form.Text>Lower = tried first</Form.Text></Form.Group>
          <div className="col-md-3 d-flex flex-column justify-content-end"><Form.Check type="switch" id="pa" label="Active" checked={active} onChange={(e) => setActive(e.target.checked)} /><Form.Check type="switch" id="pd" label="Default" checked={isDefault} onChange={(e) => setIsDefault(e.target.checked)} /></div>
          {def.fields.map((f) => (
            <Form.Group key={f.key} className={f.type === 'textarea' ? 'col-12' : 'col-md-6'}>
              <Form.Label>{f.label}{f.required && <span className="text-danger"> *</span>}</Form.Label>
              {f.secret ? <Form.Control type="password" autoComplete="new-password" placeholder={provider?.secretsConfigured.includes(f.key) ? '•••••••• saved — leave blank to keep' : ''} value={secrets[f.key] ?? ''} onChange={(e) => setSecrets({ ...secrets, [f.key]: e.target.value })} isInvalid={!!errors[f.key]} />
                : f.type === 'select' ? <Form.Select value={settings[f.key] ?? ''} onChange={(e) => setSettings({ ...settings, [f.key]: e.target.value })}>{f.options?.map((o) => <option key={o}>{o}</option>)}</Form.Select>
                : f.type === 'textarea' ? <Form.Control as="textarea" rows={3} value={settings[f.key] ?? ''} onChange={(e) => setSettings({ ...settings, [f.key]: e.target.value })} isInvalid={!!errors[f.key]} className="secret small" />
                : <Form.Control type={f.type === 'number' ? 'number' : f.type === 'email' ? 'email' : 'text'} value={settings[f.key] ?? ''} onChange={(e) => setSettings({ ...settings, [f.key]: e.target.value })} isInvalid={!!errors[f.key]} />}
              {f.help && <Form.Text>{f.help}</Form.Text>}<Form.Control.Feedback type="invalid">{errors[f.key]}</Form.Control.Feedback>
            </Form.Group>))}
        </div>
        <Alert variant="light" className="border small mt-3 mb-0"><FiKey /> Secrets are encrypted on the server (AES-256) and are never displayed again after saving.</Alert>
      </Modal.Body>
      <Modal.Footer><Button variant="outline-secondary" onClick={() => onClose(false)}>Cancel</Button><Button onClick={() => void save()}>Save provider</Button></Modal.Footer>
    </Modal>
  )
}

function TestSendModal({ provider, onClose }: { provider: Provider; onClose: () => void }) {
  const [to, setTo] = useState('')
  const [msg, setMsg] = useState('')
  const [res, setRes] = useState<{ success: boolean; message: string } | null>(null)
  const [busy, setBusy] = useState(false)
  const toast = useToast()
  const go = async () => { setBusy(true); try { setRes((await api.post(`/api/admin/communication/providers/${provider.id}/test-send`, { recipient: provider.channel === 'Push' ? 'self' : to, message: msg || null })).data) } catch (e) { toast.error(e) } finally { setBusy(false) } }
  return (
    <Modal show onHide={onClose} centered>
      <Modal.Header closeButton><Modal.Title className="h5">Send test — {provider.name}</Modal.Title></Modal.Header>
      <Modal.Body>
        {provider.channel === 'Push' ? <p className="small">A test notification will be sent to your own subscribed devices. Enable push first in Settings → Notifications on this device.</p>
          : <Form.Control className="mb-2" placeholder={provider.channel === 'Email' ? 'Recipient email' : 'Mobile number'} value={to} onChange={(e) => setTo(e.target.value)} />}
        <Form.Control as="textarea" rows={2} placeholder="Message (optional)" value={msg} onChange={(e) => setMsg(e.target.value)} />
        {res && <Alert variant={res.success ? 'success' : 'danger'} className="small mt-2 mb-0">{res.message}</Alert>}
      </Modal.Body>
      <Modal.Footer><Button disabled={busy || (provider.channel !== 'Push' && !to)} onClick={() => void go()}>{busy ? <Spinner size="sm" /> : <><FiSend /> Send</>}</Button></Modal.Footer>
    </Modal>
  )
}

interface Policy { eventType: string; displayName: string; inAppEnabled: boolean; emailEnabled: boolean; smsEnabled: boolean; pushEnabled: boolean; whatsAppEnabled: boolean; isSecurityCritical: boolean; userCanOptOut: boolean }
function Policies() {
  const toast = useToast()
  const p = useAsync(() => api.get<Policy[]>('/api/admin/communication/policies').then((r) => r.data), [])
  const [rows, setRows] = useState<Policy[]>([])
  useEffect(() => { if (p.data) setRows(p.data) }, [p.data])
  const set = (i: number, k: keyof Policy, v: boolean) => setRows(rows.map((r, j) => (j === i ? { ...r, [k]: v } : r)))
  const save = async () => { try { setRows((await api.put<Policy[]>('/api/admin/communication/policies', rows)).data); toast.success('Notification policies saved') } catch (e) { toast.error(e) } }
  if (p.loading) return <Loading />
  const cols: [keyof Policy, string][] = [['emailEnabled', 'Email'], ['smsEnabled', 'SMS'], ['pushEnabled', 'Push'], ['inAppEnabled', 'In-App'], ['whatsAppEnabled', 'WhatsApp']]
  return (
    <Card>
      <Card.Header className="d-flex justify-content-between align-items-center">Channel control matrix<Button size="sm" onClick={() => void save()}>Save matrix</Button></Card.Header>
      <Card.Body className="small text-muted pb-0">Effective delivery = platform policy (this matrix) → active provider → user preference. <FiKey /> Security-critical events always deliver in-app, must keep Email or SMS on, and users cannot opt out.</Card.Body>
      <Table responsive hover className="mb-0 matrix align-middle">
        <thead><tr><th>Event</th>{cols.map(([, l]) => <th key={l}>{l}</th>)}<th>User can opt out</th></tr></thead>
        <tbody>{rows.map((r, i) => <tr key={r.eventType}><td><span className="fw-medium">{r.displayName}</span> {r.isSecurityCritical && <Badge bg="" className="bg-soft-danger">Security</Badge>}<div className="small text-muted"><code>{r.eventType}</code></div></td>
          {cols.map(([k]) => <td key={k}><Form.Check type="switch" id={`${r.eventType}-${k}`} checked={!!r[k]} disabled={k === 'inAppEnabled' && r.isSecurityCritical} onChange={(e) => set(i, k, e.target.checked)} aria-label={`${r.displayName} ${k}`} /></td>)}
          <td><Form.Check type="switch" id={`${r.eventType}-opt`} checked={r.userCanOptOut} disabled={r.isSecurityCritical} onChange={(e) => set(i, 'userCanOptOut', e.target.checked)} /></td></tr>)}</tbody></Table>
    </Card>
  )
}

interface Tpl { id?: string; eventType: string; channel: string; subject?: string; body: string; providerTemplateId?: string; isActive: boolean; updatedAt?: string }
const PLACEHOLDERS = ['UserName', 'OTP', 'ApplicationName', 'EventDate', 'EventTime', 'Title', 'Body', 'Link', 'ExpiryMinutes', 'Browser', 'IpAddress', 'Location']
function Templates() {
  const toast = useToast()
  const confirm = useConfirm()
  const t = useAsync(() => api.get<Tpl[]>('/api/admin/communication/templates').then((r) => r.data), [])
  const policies = useAsync(() => api.get<Policy[]>('/api/admin/communication/policies').then((r) => r.data), [])
  const [ch, setCh] = useState('Email')
  const [edit, setEdit] = useState<Tpl | null>(null)
  const [preview, setPreview] = useState<{ subject?: string; body: string } | null>(null)
  const [testTo, setTestTo] = useState('')
  const name = (e: string) => policies.data?.find((p) => p.eventType === e)?.displayName ?? e
  useEffect(() => {
    if (!edit) return
    const h = setTimeout(() => { api.post('/api/admin/communication/templates/preview', edit).then((r) => setPreview(r.data)).catch(() => {}) }, 400)
    return () => clearTimeout(h)
  }, [edit])
  const save = async () => { if (!edit) return; try { if (edit.id) await api.put(`/api/admin/communication/templates/${edit.id}`, edit); else await api.post('/api/admin/communication/templates', edit); toast.success('Template saved'); setEdit(null); void t.reload() } catch (e) { toast.error(e) } }
  const items = useMemo(() => t.data?.filter((x) => x.channel === ch) ?? [], [t.data, ch])
  return (
    <>
      <div className="d-flex justify-content-between mb-3"><Nav variant="pills" activeKey={ch} onSelect={(k) => setCh(k ?? 'Email')}>{['Email', 'Sms', 'Push', 'WhatsApp'].map((c) => <Nav.Item key={c}><Nav.Link eventKey={c}>{CH_ICON[c]} {c === 'Sms' ? 'SMS' : c}</Nav.Link></Nav.Item>)}</Nav>
        <Button size="sm" onClick={() => setEdit({ eventType: policies.data?.[0]?.eventType ?? 'welcome', channel: ch, subject: '', body: '', isActive: true })}><FiPlus /> New template</Button></div>
      <Card>{items.length === 0 ? <EmptyState title="No templates for this channel" text="Without a template the event title and message are used." /> :
        <Table hover className="mb-0 small"><thead><tr><th>Event</th><th>Subject / title</th><th>Provider template ID</th><th>Status</th><th>Updated</th><th /></tr></thead>
          <tbody>{items.map((x) => <tr key={x.id} className="clickable" onClick={() => setEdit(x)}><td className="fw-medium">{name(x.eventType)}</td><td>{x.subject}</td><td>{x.providerTemplateId ?? '—'}</td><td>{x.isActive ? <Badge bg="" className="bg-soft-success">Active</Badge> : <Badge bg="secondary">Off</Badge>}</td><td>{x.updatedAt && relative(x.updatedAt)}</td>
            <td><Button size="sm" variant="link" className="text-danger" onClick={async (e) => { e.stopPropagation(); if (await confirm({ title: 'Delete template?' })) { await api.delete(`/api/admin/communication/templates/${x.id}`); void t.reload() } }}><FiTrash2 /></Button></td></tr>)}</tbody></Table>}</Card>
      <Modal show={!!edit} onHide={() => { setEdit(null); setPreview(null) }} size="xl" centered>
        <Modal.Header closeButton><Modal.Title className="h5">{edit?.id ? 'Edit' : 'New'} {edit?.channel} template</Modal.Title></Modal.Header>
        {edit && <Modal.Body><div className="row g-3">
          <div className="col-lg-6">
            <Form.Select className="mb-2" value={edit.eventType} disabled={!!edit.id} onChange={(e) => setEdit({ ...edit, eventType: e.target.value })}>{policies.data?.map((p) => <option key={p.eventType} value={p.eventType}>{p.displayName}</option>)}</Form.Select>
            {edit.channel !== 'Sms' && <Form.Control className="mb-2" placeholder="Subject / title" value={edit.subject ?? ''} onChange={(e) => setEdit({ ...edit, subject: e.target.value })} />}
            <Form.Control as="textarea" rows={12} className="secret small mb-2" value={edit.body} onChange={(e) => setEdit({ ...edit, body: e.target.value })} placeholder={edit.channel === 'Email' ? 'HTML body' : 'Message text'} />
            <div className="small mb-2">Placeholders: {PLACEHOLDERS.map((p) => <span key={p} className="tag clickable" onClick={() => setEdit({ ...edit, body: edit.body + `{{${p}}}` })}>{`{{${p}}}`}</span>)}</div>
            {edit.channel !== 'Email' && <Form.Control className="mb-2" placeholder={edit.channel === 'Sms' ? 'DLT / provider template ID' : 'Approved template name'} value={edit.providerTemplateId ?? ''} onChange={(e) => setEdit({ ...edit, providerTemplateId: e.target.value })} />}
            <Form.Check type="switch" id="tact" label="Active" checked={edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} />
          </div>
          <div className="col-lg-6"><div className="small fw-semibold mb-1">Preview (sample values)</div>
            {preview?.subject && <div className="small mb-1"><strong>Subject:</strong> {preview.subject}</div>}
            {edit.channel === 'Email' ? <iframe title="Email preview" sandbox="" srcDoc={preview?.body ?? ''} style={{ width: '100%', height: 380, border: '1px solid var(--nm-border)', borderRadius: 10, background: '#fff' }} />
              : <div className="border rounded p-3 small" style={{ whiteSpace: 'pre-wrap' }}>{preview?.body}</div>}
            {edit.channel === 'Sms' && preview && <div className="small text-muted mt-1">{preview.body.length} characters · {Math.ceil(preview.body.length / 160)} SMS part(s)</div>}
            <div className="d-flex gap-2 mt-2"><Form.Control size="sm" placeholder={edit.channel === 'Email' ? 'Test email address' : 'Test mobile'} value={testTo} onChange={(e) => setTestTo(e.target.value)} />
              <Button size="sm" variant="outline-primary" disabled={!testTo} onClick={async () => { try { const r = await api.post('/api/admin/communication/templates/test', edit, { params: { recipient: testTo } }); toast.push(r.data.success ? 'success' : 'error', r.data.message) } catch (e) { toast.error(e) } }}>Send test</Button></div>
          </div></div></Modal.Body>}
        <Modal.Footer><Button variant="outline-secondary" onClick={() => setEdit(null)}>Cancel</Button><Button onClick={() => void save()}>Save template</Button></Modal.Footer>
      </Modal>
    </>
  )
}

function Logs() {
  const [f, setF] = useState({ channel: '', status: '', eventType: '', search: '' })
  const [page, setPage] = useState(1)
  const q = useAsync(() => api.get('/api/admin/communication/queue').then((r) => r.data as Record<string, number>), [])
  const logs = useAsync(() => api.get('/api/admin/communication/logs', { params: { channel: f.channel || undefined, status: f.status || undefined, eventType: f.eventType || undefined, search: f.search || undefined, page, pageSize: 50 } })
    .then((r) => r.data as { items: { id: string; createdAt: string; maskedRecipient: string; channel: string; eventType: string; providerName?: string; status: string; failureReason?: string; attempt: number; willRetry: boolean }[]; totalPages: number }), [f.channel, f.status, f.eventType, f.search, page])
  return (
    <>
      {q.data && <div className="d-flex flex-wrap gap-2 mb-3 small">{Object.entries(q.data).map(([k, v]) => <Badge key={k} bg="" className="badge-soft fs-6 fw-normal">{k}: <strong>{v}</strong></Badge>)}</div>}
      <Card className="mb-3"><Card.Body className="row g-2">
        <div className="col-6 col-md-2"><Form.Select size="sm" value={f.channel} onChange={(e) => setF({ ...f, channel: e.target.value })}><option value="">All channels</option>{['Email', 'Sms', 'Push', 'WhatsApp'].map((c) => <option key={c}>{c}</option>)}</Form.Select></div>
        <div className="col-6 col-md-2"><Form.Select size="sm" value={f.status} onChange={(e) => setF({ ...f, status: e.target.value })}><option value="">All statuses</option>{['Sent', 'Failed', 'Dead'].map((c) => <option key={c}>{c}</option>)}</Form.Select></div>
        <div className="col-6 col-md-3"><Form.Control size="sm" placeholder="Event type" value={f.eventType} onChange={(e) => setF({ ...f, eventType: e.target.value })} /></div>
        <div className="col-6 col-md-3"><Form.Control size="sm" placeholder="Recipient / provider / error" value={f.search} onChange={(e) => setF({ ...f, search: e.target.value })} /></div>
      </Card.Body></Card>
      <Card>{logs.loading && !logs.data ? <Loading /> : <Table responsive size="sm" hover className="mb-0 small">
        <thead><tr><th>Time</th><th>Recipient</th><th>Channel</th><th>Event</th><th>Provider</th><th>Status</th><th>Attempt</th><th>Failure reason</th></tr></thead>
        <tbody>{logs.data?.items.map((l) => <tr key={l.id}><td className="text-nowrap">{dateTime(l.createdAt)}</td><td>{l.maskedRecipient}</td><td>{l.channel}</td><td><code>{l.eventType}</code></td><td>{l.providerName ?? '—'}</td><td><StatusBadge status={l.status} /></td><td>{l.attempt}{l.willRetry && <Badge bg="" className="bg-soft-info ms-1">retrying</Badge>}</td><td className="text-danger">{l.failureReason}</td></tr>)}</tbody></Table>}
        <Card.Footer><Pager page={page} totalPages={logs.data?.totalPages ?? 1} onChange={setPage} /></Card.Footer></Card>
      <div className="small text-muted mt-2">Recipients are masked; OTP values, message bodies and provider secrets are never stored in logs.</div>
    </>
  )
}

function Announcements() {
  const toast = useToast()
  const list = useAsync(() => api.get('/api/admin/communication/announcements').then((r) => r.data as { items: { id: string; title: string; body: string; target: string; targetValue?: string; sendInApp: boolean; sendEmail: boolean; sendPush: boolean; sendSms: boolean; recipientCount: number; createdByName: string; createdAt: string }[] }), [])
  const [a, setA] = useState({ title: '', body: '', target: 'AllUsers', targetValue: '', sendInApp: true, sendEmail: false, sendPush: true, sendSms: false })
  const [busy, setBusy] = useState(false)
  const confirm = useConfirm()
  const send = async () => {
    if (!(await confirm({ title: 'Send this announcement?', body: 'It will be delivered immediately to the selected audience.', confirmText: 'Send', variant: 'primary' }))) return
    setBusy(true)
    try { const r = await api.post('/api/admin/communication/announcements', a); toast.success(`Sent to ${r.data.recipientCount} user(s)`); setA({ ...a, title: '', body: '' }); void list.reload() } catch (e) { toast.error(e) } finally { setBusy(false) }
  }
  return (
    <div className="row g-3">
      <div className="col-lg-5"><Card><Card.Header>New announcement</Card.Header><Card.Body>
        <Form.Control className="mb-2" placeholder="Title" value={a.title} onChange={(e) => setA({ ...a, title: e.target.value })} />
        <Form.Control as="textarea" rows={5} className="mb-2" placeholder="Message" value={a.body} onChange={(e) => setA({ ...a, body: e.target.value })} />
        <Form.Select className="mb-2" value={a.target} onChange={(e) => setA({ ...a, target: e.target.value })}><option value="AllUsers">All users</option><option value="ActiveUsers">Active users (last 30 days)</option><option value="SelectedUsers">Selected users (NijMitra IDs)</option><option value="Role">Users with a role</option></Form.Select>
        {a.target === 'SelectedUsers' && <Form.Control as="textarea" rows={2} className="mb-2" placeholder="NM100025, NM100026…" value={a.targetValue} onChange={(e) => setA({ ...a, targetValue: e.target.value })} />}
        {a.target === 'Role' && <Form.Select className="mb-2" value={a.targetValue} onChange={(e) => setA({ ...a, targetValue: e.target.value })}><option value="">Choose role</option><option>SuperAdmin</option><option>Admin</option><option>SupportAdmin</option></Form.Select>}
        <div className="mb-3">{([['sendInApp', 'In-app'], ['sendEmail', 'Email'], ['sendPush', 'Push'], ['sendSms', 'SMS']] as const).map(([k, l]) => <Form.Check inline key={k} id={k} label={l} checked={a[k]} onChange={(e) => setA({ ...a, [k]: e.target.checked })} />)}</div>
        {a.sendSms && <Alert variant="warning" className="small py-2">SMS announcements cost money and in India need a DLT-approved template. Use only when appropriate.</Alert>}
        <Button disabled={busy || !a.title || !a.body} onClick={() => void send()}>{busy ? <Spinner size="sm" /> : <><FiSend /> Send announcement</>}</Button>
      </Card.Body></Card></div>
      <div className="col-lg-7"><Card><Card.Header>History</Card.Header><Card.Body>
        {list.data?.items.length === 0 && <div className="small text-muted">No announcements yet.</div>}
        {list.data?.items.map((x) => <div key={x.id} className="list-row align-items-start"><div className="flex-grow-1"><div className="fw-semibold">{x.title}</div><div className="small">{x.body}</div>
          <div className="small text-muted">{dateTime(x.createdAt)} · by {x.createdByName} · {x.recipientCount} recipients · {[x.sendInApp && 'in-app', x.sendEmail && 'email', x.sendPush && 'push', x.sendSms && 'SMS'].filter(Boolean).join(', ')}</div></div></div>)}
      </Card.Body></Card></div>
    </div>
  )
}
