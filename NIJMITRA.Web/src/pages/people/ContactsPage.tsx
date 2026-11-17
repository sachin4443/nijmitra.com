import { useEffect, useState } from 'react'
import { Badge, Button, Card, Form, Modal, Spinner, Table } from 'react-bootstrap'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { FiArrowLeft, FiCamera, FiEdit2, FiMail, FiPaperclip, FiPhone, FiPlus, FiTrash2, FiUsers } from 'react-icons/fi'
import { api, downloadFile, uploadFile } from '../../lib/api'
import { useAsync, useDebounced } from '../../lib/hooks'
import { date, dateTime, money } from '../../lib/format'
import { useToast } from '../../contexts/ToastContext'
import { AttachmentList, Avatar, EmptyState, ExportMenu, LinkButton, Loading, PageHeader, Pager, PreviewModal, StatCard, type FileInfo, useConfirm } from '../../components/ui'

export interface Contact { id: string; name: string; mobile?: string; alternateMobile?: string; email?: string; address?: string; company?: string; category?: string; groupId?: string; groupName?: string; notes?: string; avatarFileId?: string; attachments: FileInfo[] }
interface Group { id: string; name: string; color?: string; contactCount: number }

export default function ContactsPage() {
  const { id } = useParams()
  return id ? <ContactDetail id={id} /> : <ContactList />
}

function ContactList() {
  const nav = useNavigate()
  const toast = useToast()
  const [params, setParams] = useSearchParams()
  const groups = useAsync(() => api.get<Group[]>('/api/contacts/groups').then((r) => r.data), [])
  const [group, setGroup] = useState('')
  const [q, setQ] = useState('')
  const search = useDebounced(q)
  const [page, setPage] = useState(1)
  const list = useAsync(() => api.get('/api/contacts', { params: { groupId: group || undefined, search: search || undefined, page, pageSize: 30 } }).then((r) => r.data as { items: Contact[]; totalPages: number; totalCount: number }), [group, search, page])
  const [edit, setEdit] = useState<Partial<Contact> | null>(null)
  useEffect(() => { if (params.get('new')) { setEdit({}); params.delete('new'); setParams(params, { replace: true }) } }, [params, setParams])
  const addGroup = async () => { const n = prompt('Group name'); if (n?.trim()) { try { await api.post('/api/contacts/groups', { name: n.trim() }); void groups.reload() } catch (e) { toast.error(e) } } }
  return (
    <>
      <PageHeader title="Contacts" subtitle="Family, friends, business contacts, customers and suppliers" icon={<FiUsers />}>
        <ExportMenu url={(f) => `/api/contacts/export?format=${f}${group ? `&groupId=${group}` : ''}`} />
        <Button onClick={() => setEdit({})}><FiPlus /> Add contact</Button>
      </PageHeader>
      <div className="row g-3">
        <div className="col-lg-3"><Card><Card.Body className="p-2">
          <div className={`nm-nav-link ${!group ? 'active' : ''}`} role="button" onClick={() => setGroup('')}>All contacts</div>
          <div className="nm-nav-label d-flex justify-content-between">Groups <Button size="sm" variant="link" className="p-0" onClick={() => void addGroup()}><FiPlus /></Button></div>
          {groups.data?.map((g) => <div key={g.id} className={`nm-nav-link py-1 ${group === g.id ? 'active' : ''}`} role="button" onClick={() => { setGroup(g.id); setPage(1) }}><span className="flex-grow-1">{g.name}</span><small>{g.contactCount}</small></div>)}
        </Card.Body></Card></div>
        <div className="col-lg-9">
          <Form.Control className="mb-3" placeholder="Search by name, mobile, email or company…" value={q} onChange={(e) => { setQ(e.target.value); setPage(1) }} />
          <Card>
            {list.loading && !list.data ? <Loading /> : list.data?.items.length === 0 ? <EmptyState icon={<FiUsers />} title="No contacts" action={<Button onClick={() => setEdit({})}><FiPlus /> Add contact</Button>} /> :
              <Card.Body>{list.data?.items.map((c) => (
                <div key={c.id} className="list-row clickable" onClick={() => nav(`/contacts/${c.id}`)}>
                  <Avatar name={c.name} src={c.avatarFileId ? `/api/files/${c.avatarFileId}?inline=true` : null} size={42} />
                  <div className="flex-grow-1 min-w-0"><div className="fw-medium">{c.name} {c.groupName && <Badge bg="" className="badge-soft">{c.groupName}</Badge>}</div>
                    <div className="small text-muted truncate">{[c.mobile, c.email, c.company].filter(Boolean).join(' · ')}</div></div>
                  {c.mobile && <a href={`tel:${c.mobile}`} className="btn btn-sm btn-link" onClick={(e) => e.stopPropagation()} aria-label="Call"><FiPhone /></a>}
                </div>))}</Card.Body>}
            <Card.Footer className="d-flex justify-content-between"><small className="text-muted">{list.data?.totalCount ?? 0} contact(s)</small><Pager page={page} totalPages={list.data?.totalPages ?? 1} onChange={setPage} /></Card.Footer>
          </Card>
        </div>
      </div>
      {edit && <ContactModal value={edit} groups={groups.data ?? []} onClose={(saved) => { setEdit(null); if (saved) { void list.reload(); void groups.reload(); if (!edit.id) nav(`/contacts/${saved}`) } }} />}
    </>
  )
}

export function ContactModal({ value, groups, onClose }: { value: Partial<Contact>; groups: Group[]; onClose: (savedId?: string) => void }) {
  const toast = useToast()
  const [c, setC] = useState<Partial<Contact>>(value)
  const [busy, setBusy] = useState(false)
  const set = (k: keyof Contact) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) => setC({ ...c, [k]: e.target.value })
  const save = async () => {
    setBusy(true)
    try {
      const body = { ...c, groupId: c.groupId || null, email: c.email || null }
      const r = value.id ? await api.put(`/api/contacts/${value.id}`, body) : await api.post('/api/contacts', body)
      toast.success('Contact saved'); onClose(r.data.id)
    } catch (x) { toast.error(x) } finally { setBusy(false) }
  }
  return (
    <Modal show onHide={() => onClose()} size="lg" centered>
      <Modal.Header closeButton><Modal.Title className="h5">{value.id ? 'Edit contact' : 'Add contact'}</Modal.Title></Modal.Header>
      <Modal.Body className="row g-3">
        <Form.Group className="col-md-6"><Form.Label>Name *</Form.Label><Form.Control value={c.name ?? ''} onChange={set('name')} /></Form.Group>
        <Form.Group className="col-md-6"><Form.Label>Company</Form.Label><Form.Control value={c.company ?? ''} onChange={set('company')} /></Form.Group>
        <Form.Group className="col-md-6"><Form.Label>Mobile</Form.Label><Form.Control type="tel" value={c.mobile ?? ''} onChange={set('mobile')} /></Form.Group>
        <Form.Group className="col-md-6"><Form.Label>Alternate mobile</Form.Label><Form.Control type="tel" value={c.alternateMobile ?? ''} onChange={set('alternateMobile')} /></Form.Group>
        <Form.Group className="col-md-6"><Form.Label>Email</Form.Label><Form.Control type="email" value={c.email ?? ''} onChange={set('email')} /></Form.Group>
        <Form.Group className="col-md-3"><Form.Label>Group</Form.Label><Form.Select value={c.groupId ?? ''} onChange={set('groupId')}><option value="">—</option>{groups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}</Form.Select></Form.Group>
        <Form.Group className="col-md-3"><Form.Label>Category</Form.Label><Form.Control value={c.category ?? ''} onChange={set('category')} placeholder="e.g. Doctor" /></Form.Group>
        <Form.Group className="col-12"><Form.Label>Address</Form.Label><Form.Control as="textarea" rows={2} value={c.address ?? ''} onChange={set('address')} /></Form.Group>
        <Form.Group className="col-12"><Form.Label>Notes</Form.Label><Form.Control as="textarea" rows={2} value={c.notes ?? ''} onChange={set('notes')} /></Form.Group>
      </Modal.Body>
      <Modal.Footer><Button variant="outline-secondary" onClick={() => onClose()}>Cancel</Button><Button disabled={busy || !c.name?.trim()} onClick={() => void save()}>{busy ? <Spinner size="sm" /> : 'Save'}</Button></Modal.Footer>
    </Modal>
  )
}

interface Summary { contact: Contact; moneyGiven: number; moneyReceived: number; moneyBorrowed: number; moneyRepaid: number; receivable: number; payable: number; netBalance: number
  transactions: { type: string; id: string; title: string; amount?: number; direction?: string; date: string }[]; reminders: { id: string; title: string; at: string; source: string }[] }

function ContactDetail({ id }: { id: string }) {
  const nav = useNavigate()
  const toast = useToast()
  const confirm = useConfirm()
  const s = useAsync(() => api.get<Summary>(`/api/contacts/${id}/summary`).then((r) => r.data), [id])
  const groups = useAsync(() => api.get<Group[]>('/api/contacts/groups').then((r) => r.data), [])
  const [edit, setEdit] = useState(false)
  const [preview, setPreview] = useState<FileInfo | null>(null)
  if (s.loading && !s.data) return <Loading />
  if (!s.data) return null
  const c = s.data.contact
  return (
    <>
      <PageHeader title={c.name} subtitle={[c.company, c.groupName, c.category].filter(Boolean).join(' · ')}>
        <Button size="sm" variant="outline-secondary" onClick={() => nav('/contacts')}><FiArrowLeft /> Contacts</Button>
        <Button size="sm" variant="outline-secondary" onClick={() => setEdit(true)}><FiEdit2 /> Edit</Button>
        <LinkButton size="sm" to={`/udhar?new=1&contact=${id}`}><FiPlus /> Udhar entry</LinkButton>
        <Button size="sm" variant="outline-danger" onClick={async () => { if (await confirm({ title: `Delete ${c.name}?` })) { await api.delete(`/api/contacts/${id}`); nav('/contacts') } }}><FiTrash2 /></Button>
      </PageHeader>
      <div className="row g-3">
        <div className="col-lg-4"><Card className="h-100"><Card.Body className="text-center">
          <div className="position-relative d-inline-block mb-2"><Avatar name={c.name} src={c.avatarFileId ? `/api/files/${c.avatarFileId}?inline=true` : null} size={96} />
            <label className="btn btn-sm btn-primary rounded-circle position-absolute bottom-0 end-0"><FiCamera /><input type="file" hidden accept="image/*" onChange={async (e) => { const f = e.target.files?.[0]; if (f) try { await uploadFile(`/api/contacts/${id}/avatar`, f); void s.reload() } catch (x) { toast.error(x) } }} /></label></div>
          <div className="text-start small mt-2">
            {c.mobile && <div className="list-row"><FiPhone /><a href={`tel:${c.mobile}`}>{c.mobile}</a></div>}
            {c.alternateMobile && <div className="list-row"><FiPhone /><a href={`tel:${c.alternateMobile}`}>{c.alternateMobile}</a></div>}
            {c.email && <div className="list-row"><FiMail /><a href={`mailto:${c.email}`}>{c.email}</a></div>}
            {c.address && <div className="list-row">{c.address}</div>}
            {c.notes && <div className="list-row" style={{ whiteSpace: 'pre-wrap' }}>{c.notes}</div>}
          </div>
        </Card.Body></Card></div>
        <div className="col-lg-8">
          <div className="row g-3 mb-3">
            <div className="col-6 col-md-3"><StatCard label="Money given" value={money(s.data.moneyGiven)} icon={<FiPlus />} tone="warning" /></div>
            <div className="col-6 col-md-3"><StatCard label="Money received" value={money(s.data.moneyReceived)} icon={<FiPlus />} tone="success" /></div>
            <div className="col-6 col-md-3"><StatCard label="To receive" value={money(s.data.receivable)} icon={<FiPlus />} tone="info" /></div>
            <div className="col-6 col-md-3"><StatCard label="Net balance" value={money(s.data.netBalance)} icon={<FiPlus />} tone={s.data.netBalance >= 0 ? 'success' : 'danger'} hint={s.data.netBalance >= 0 ? 'They owe you' : 'You owe them'} /></div>
          </div>
          <Card className="mb-3"><Card.Header className="d-flex justify-content-between align-items-center">Transactions
            {s.data.transactions.some((t) => t.type.startsWith('udhar')) && <Button size="sm" variant="outline-primary" onClick={() => void downloadFile(`/api/udhar/statement?contactId=${id}&format=pdf`)}>Statement PDF</Button>}</Card.Header>
            {s.data.transactions.length === 0 ? <Card.Body className="small text-muted">No transactions linked to this contact yet.</Card.Body> :
              <Table responsive size="sm" className="mb-0 small"><thead><tr><th>Date</th><th>Type</th><th>Details</th><th className="text-end">Amount</th></tr></thead>
                <tbody>{s.data.transactions.map((t, i) => <tr key={i}><td>{date(t.date)}</td><td><Badge bg="" className="badge-soft">{t.type.replace('-', ' ')}</Badge></td><td>{t.title} <span className="text-muted">{t.direction}</span></td>
                  <td className={`text-end ${t.direction === 'Received' || t.direction === 'Borrowed' ? 'money-in' : 'money-out'}`}>{money(t.amount)}</td></tr>)}</tbody></Table>}
          </Card>
          <div className="row g-3">
            <div className="col-md-6"><Card className="h-100"><Card.Header>Reminders</Card.Header><Card.Body className="small">{s.data.reminders.length === 0 ? <span className="text-muted">No upcoming reminders.</span> : s.data.reminders.map((r) => <div key={r.id + r.at} className="list-row">{r.title}<span className="ms-auto text-muted">{dateTime(r.at)}</span></div>)}</Card.Body></Card></div>
            <div className="col-md-6"><Card className="h-100"><Card.Header className="d-flex justify-content-between align-items-center">Documents
              <label className="btn btn-sm btn-outline-primary mb-0"><FiPaperclip /> Attach<input type="file" hidden accept=".pdf,.doc,.docx,.xls,.xlsx,.csv,.jpg,.jpeg,.png,.webp,.txt" onChange={async (e) => { const f = e.target.files?.[0]; if (f) try { await uploadFile(`/api/contacts/${id}/attachments`, f); void s.reload() } catch (x) { toast.error(x) } }} /></label></Card.Header>
              <Card.Body><AttachmentList files={c.attachments} urlFor={(f) => `/api/files/${f.id}`} onPreview={setPreview} onRemove={async (f) => { await api.delete(`/api/contacts/${id}/attachments/${f.id}`); void s.reload() }} /></Card.Body></Card></div>
          </div>
        </div>
      </div>
      {edit && <ContactModal value={c} groups={groups.data ?? []} onClose={(saved) => { setEdit(false); if (saved) void s.reload() }} />}
      <PreviewModal url={preview ? `/api/files/${preview.id}?inline=true` : null} name={preview?.name} contentType={preview?.contentType} onHide={() => setPreview(null)} />
    </>
  )
}
