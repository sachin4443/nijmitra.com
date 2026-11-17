import { useEffect, useState } from 'react'
import { Badge, Button, Card, Form, Modal, Nav, ProgressBar, Spinner, Table } from 'react-bootstrap'
import { useSearchParams } from 'react-router-dom'
import { FiArrowDownLeft, FiArrowUpRight, FiCheckCircle, FiClock, FiDownload, FiPaperclip, FiPlus, FiTrash2 } from 'react-icons/fi'
import { RiHandCoinLine } from 'react-icons/ri'
import { api, downloadFile, uploadFile } from '../../lib/api'
import { useAsync, useDebounced } from '../../lib/hooks'
import { date, dateTime, localInput, money, today } from '../../lib/format'
import { useToast } from '../../contexts/ToastContext'
import { AttachmentList, EmptyState, ExportMenu, Loading, PageHeader, Pager, StatCard, StatusBadge, type FileInfo, useConfirm } from '../../components/ui'
import { PAYMENT_METHODS } from '../finance/FinancePage'
import type { Contact } from './ContactsPage'

interface Payment { id: string; amount: number; paymentDate: string; method: string; note?: string }
interface Credit { id: string; direction: 'Given' | 'Borrowed'; contactId?: string; personName: string; contactNumber?: string; amount: number; settledAmount: number; outstanding: number; transactionDate: string; expectedReturnDate?: string; nextReminderAt?: string; description?: string; status: string; isOverdue: boolean; payments: Payment[]; attachments: FileInfo[] }
interface Summary { totalReceivable: number; totalPayable: number; amountReceived: number; amountPaid: number; totalGiven: number; totalBorrowed: number; netBalance: number; overdueCount: number; overdueAmount: number; upcomingReminders: Credit[]; people: { personName: string; contactId?: string; receivable: number; payable: number; net: number }[] }

export default function UdharPage() {
  const toast = useToast()
  const confirm = useConfirm()
  const [params, setParams] = useSearchParams()
  const [dir, setDir] = useState<'' | 'Given' | 'Borrowed'>('')
  const [status, setStatus] = useState('')
  const [overdue, setOverdue] = useState(false)
  const [q, setQ] = useState('')
  const search = useDebounced(q)
  const [page, setPage] = useState(1)
  const summary = useAsync(() => api.get<Summary>('/api/udhar/summary').then((r) => r.data), [])
  const list = useAsync(() => api.get('/api/udhar', { params: { direction: dir || undefined, status: status || undefined, overdueOnly: overdue || undefined, search: search || undefined, page, pageSize: 20 } }).then((r) => r.data as { items: Credit[]; totalPages: number; totalCount: number }), [dir, status, overdue, search, page])
  const [edit, setEdit] = useState<Partial<Credit> | null>(null)
  const [view, setView] = useState<Credit | null>(null)
  const reload = () => { void list.reload(); void summary.reload() }
  useEffect(() => {
    if (params.get('new')) { setEdit({ direction: 'Given', contactId: params.get('contact') ?? undefined }); params.delete('new'); params.delete('contact'); setParams(params, { replace: true }) }
    const open = params.get('open')
    if (open) { api.get<Credit>(`/api/udhar/${open}`).then((r) => setView(r.data)).catch(() => {}); params.delete('open'); setParams(params, { replace: true }) }
  }, [params, setParams])
  const s = summary.data
  return (
    <>
      <PageHeader title="Udhar Manager" subtitle="किससे लेना है, किसे देना है — पूरा हिसाब" icon={<RiHandCoinLine />}>
        <ExportMenu url={(f) => `/api/udhar/export?format=${f}`} formats={['xlsx', 'pdf']} />
        <Button onClick={() => setEdit({ direction: 'Given' })}><FiPlus /> New entry</Button>
      </PageHeader>
      {s && <div className="row g-3 mb-3">
        <div className="col-6 col-xl-3"><StatCard label="Total receivable" value={money(s.totalReceivable)} icon={<FiArrowDownLeft />} tone="success" hint={`Received so far ${money(s.amountReceived)}`} /></div>
        <div className="col-6 col-xl-3"><StatCard label="Total payable" value={money(s.totalPayable)} icon={<FiArrowUpRight />} tone="warning" hint={`Paid so far ${money(s.amountPaid)}`} /></div>
        <div className="col-6 col-xl-3"><StatCard label="Outstanding balance" value={money(s.netBalance)} icon={<RiHandCoinLine />} tone={s.netBalance >= 0 ? 'info' : 'danger'} hint={s.netBalance >= 0 ? 'Others owe you' : 'You owe others'} /></div>
        <div className="col-6 col-xl-3"><StatCard label="Overdue" value={s.overdueCount} icon={<FiClock />} tone="danger" hint={money(s.overdueAmount)} onClick={() => setOverdue(true)} /></div>
      </div>}
      <div className="row g-3">
        <div className="col-xl-8">
          <Card className="mb-3"><Card.Body className="d-flex flex-wrap gap-2 align-items-center">
            <Nav variant="pills" activeKey={dir} onSelect={(k) => { setDir((k ?? '') as typeof dir); setPage(1) }}><Nav.Item><Nav.Link eventKey="">All</Nav.Link></Nav.Item><Nav.Item><Nav.Link eventKey="Given">Money given</Nav.Link></Nav.Item><Nav.Item><Nav.Link eventKey="Borrowed">Money borrowed</Nav.Link></Nav.Item></Nav>
            <Form.Select size="sm" value={status} onChange={(e) => setStatus(e.target.value)} style={{ maxWidth: 170 }}><option value="">Any status</option><option value="Open">Open</option><option value="PartiallySettled">Partially settled</option><option value="Settled">Settled</option></Form.Select>
            <Form.Check type="switch" id="od" label="Overdue only" checked={overdue} onChange={(e) => setOverdue(e.target.checked)} />
            <Form.Control size="sm" placeholder="Search person…" value={q} onChange={(e) => setQ(e.target.value)} style={{ maxWidth: 200 }} className="ms-auto" />
          </Card.Body></Card>
          <Card>
            {list.loading && !list.data ? <Loading /> : list.data?.items.length === 0 ? <EmptyState icon={<RiHandCoinLine />} title="No entries" text="Record money you gave or borrowed to track repayments." /> :
              <Table responsive hover className="mb-0 align-middle small">
                <thead><tr><th>Person</th><th>Type</th><th className="text-end">Amount</th><th>Settled</th><th>Return by</th><th>Status</th></tr></thead>
                <tbody>{list.data?.items.map((c) => (
                  <tr key={c.id} className="clickable" onClick={() => setView(c)}>
                    <td><div className="fw-medium">{c.personName}</div><div className="text-muted">{date(c.transactionDate)}{c.description && ` · ${c.description}`}</div></td>
                    <td>{c.direction === 'Given' ? <Badge bg="" className="bg-soft-warning"><FiArrowUpRight /> Given</Badge> : <Badge bg="" className="bg-soft-info"><FiArrowDownLeft /> Borrowed</Badge>}</td>
                    <td className="text-end fw-semibold">{money(c.amount)}<div className="text-muted fw-normal">due {money(c.outstanding)}</div></td>
                    <td style={{ minWidth: 100 }}><ProgressBar now={(c.settledAmount / c.amount) * 100} variant="success" style={{ height: 6 }} /></td>
                    <td>{c.expectedReturnDate ? <span className={c.isOverdue ? 'text-danger fw-semibold' : ''}>{date(c.expectedReturnDate)}</span> : '—'}</td>
                    <td>{c.isOverdue ? <StatusBadge status="Overdue" /> : <StatusBadge status={c.status} />}</td>
                  </tr>))}</tbody></Table>}
            <Card.Footer><Pager page={page} totalPages={list.data?.totalPages ?? 1} onChange={setPage} /></Card.Footer>
          </Card>
        </div>
        <div className="col-xl-4">
          <Card className="mb-3"><Card.Header>Person-wise balance</Card.Header><Card.Body className="small">
            {s?.people.length === 0 && <span className="text-muted">All settled!</span>}
            {s?.people.map((p) => <div key={p.personName + p.contactId} className="list-row"><span className="flex-grow-1 fw-medium">{p.personName}</span>
              <span className={p.net >= 0 ? 'money-in' : 'money-out'}>{money(Math.abs(p.net))}</span>
              <Button size="sm" variant="link" title="Statement" onClick={() => void downloadFile(`/api/udhar/statement?${p.contactId ? `contactId=${p.contactId}` : `person=${encodeURIComponent(p.personName)}`}&format=pdf`)}><FiDownload /></Button></div>)}
          </Card.Body></Card>
          <Card><Card.Header>Upcoming reminders</Card.Header><Card.Body className="small">
            {s?.upcomingReminders.length === 0 && <span className="text-muted">Nothing due in the next 30 days.</span>}
            {s?.upcomingReminders.map((c) => <div key={c.id} className="list-row clickable" onClick={() => setView(c)}><FiClock className="text-warning" /><span className="flex-grow-1">{c.personName} · {money(c.outstanding)}</span><span className="text-muted">{c.nextReminderAt ? dateTime(c.nextReminderAt) : date(c.expectedReturnDate)}</span></div>)}
          </Card.Body></Card>
        </div>
      </div>
      {edit && <CreditModal value={edit} onClose={(ok) => { setEdit(null); if (ok) reload() }} />}
      {view && <CreditDetail credit={view} onChange={(c) => { setView(c); reload() }} onEdit={() => { setEdit(view); setView(null) }}
        onDelete={async () => { if (await confirm({ title: 'Delete this entry?', body: 'All recorded payments will also be removed.' })) { try { await api.delete(`/api/udhar/${view.id}`); setView(null); reload() } catch (x) { toast.error(x) } } }} onClose={() => setView(null)} />}
    </>
  )
}

function CreditModal({ value, onClose }: { value: Partial<Credit>; onClose: (ok: boolean) => void }) {
  const toast = useToast()
  const contacts = useAsync(() => api.get('/api/contacts', { params: { pageSize: 200 } }).then((r) => r.data.items as Contact[]), [])
  const [c, setC] = useState({ direction: value.direction ?? 'Given', contactId: value.contactId ?? '', personName: value.personName ?? '', contactNumber: value.contactNumber ?? '', amount: value.amount?.toString() ?? '',
    transactionDate: value.transactionDate ?? today(), expectedReturnDate: value.expectedReturnDate ?? '', nextReminderAt: value.nextReminderAt ? localInput(value.nextReminderAt) : '', description: value.description ?? '' })
  const [busy, setBusy] = useState(false)
  const save = async () => {
    setBusy(true)
    try {
      const body = { ...c, contactId: c.contactId || null, amount: Number(c.amount), expectedReturnDate: c.expectedReturnDate || null, nextReminderAt: c.nextReminderAt ? new Date(c.nextReminderAt).toISOString() : null }
      if (value.id) await api.put(`/api/udhar/${value.id}`, body); else await api.post('/api/udhar', body)
      toast.success('Entry saved'); onClose(true)
    } catch (x) { toast.error(x) } finally { setBusy(false) }
  }
  return (
    <Modal show onHide={() => onClose(false)} centered size="lg">
      <Modal.Header closeButton><Modal.Title className="h5">{value.id ? 'Edit entry' : 'New Udhar entry'}</Modal.Title></Modal.Header>
      <Modal.Body className="row g-3">
        <div className="col-12"><Nav variant="pills" activeKey={c.direction} onSelect={(k) => setC({ ...c, direction: k as 'Given' | 'Borrowed' })}>
          <Nav.Item><Nav.Link eventKey="Given"><FiArrowUpRight /> I gave money</Nav.Link></Nav.Item><Nav.Item><Nav.Link eventKey="Borrowed"><FiArrowDownLeft /> I borrowed money</Nav.Link></Nav.Item></Nav></div>
        <Form.Group className="col-md-6"><Form.Label>Contact</Form.Label><Form.Select value={c.contactId} onChange={(e) => setC({ ...c, contactId: e.target.value })}><option value="">— Not in contacts —</option>{contacts.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Form.Select></Form.Group>
        {!c.contactId && <>
          <Form.Group className="col-md-6"><Form.Label>Person name *</Form.Label><Form.Control value={c.personName} onChange={(e) => setC({ ...c, personName: e.target.value })} /></Form.Group>
          <Form.Group className="col-md-6"><Form.Label>Contact number</Form.Label><Form.Control type="tel" value={c.contactNumber} onChange={(e) => setC({ ...c, contactNumber: e.target.value })} /></Form.Group></>}
        <Form.Group className="col-md-6"><Form.Label>Amount (₹) *</Form.Label><Form.Control type="number" min="0.01" step="0.01" value={c.amount} onChange={(e) => setC({ ...c, amount: e.target.value })} /></Form.Group>
        <Form.Group className="col-md-4"><Form.Label>Transaction date</Form.Label><Form.Control type="date" value={c.transactionDate} onChange={(e) => setC({ ...c, transactionDate: e.target.value })} /></Form.Group>
        <Form.Group className="col-md-4"><Form.Label>Expected return date</Form.Label><Form.Control type="date" value={c.expectedReturnDate} onChange={(e) => setC({ ...c, expectedReturnDate: e.target.value })} /></Form.Group>
        <Form.Group className="col-md-4"><Form.Label>Next reminder</Form.Label><Form.Control type="datetime-local" value={c.nextReminderAt} onChange={(e) => setC({ ...c, nextReminderAt: e.target.value })} /></Form.Group>
        <Form.Group className="col-12"><Form.Label>Description</Form.Label><Form.Control as="textarea" rows={2} value={c.description} onChange={(e) => setC({ ...c, description: e.target.value })} /></Form.Group>
      </Modal.Body>
      <Modal.Footer><Button variant="outline-secondary" onClick={() => onClose(false)}>Cancel</Button><Button disabled={busy || !c.amount || (!c.contactId && !c.personName)} onClick={() => void save()}>{busy ? <Spinner size="sm" /> : 'Save'}</Button></Modal.Footer>
    </Modal>
  )
}

function CreditDetail({ credit, onChange, onEdit, onDelete, onClose }: { credit: Credit; onChange: (c: Credit) => void; onEdit: () => void; onDelete: () => void; onClose: () => void }) {
  const toast = useToast()
  const [p, setP] = useState({ amount: '', paymentDate: today(), method: 'Upi', note: '' })
  const pay = async (full = false) => {
    try { const r = await api.post<Credit>(`/api/udhar/${credit.id}/payments`, { ...p, amount: full ? credit.outstanding : Number(p.amount) }); onChange(r.data); setP({ ...p, amount: '', note: '' }); toast.success('Payment recorded') } catch (x) { toast.error(x) }
  }
  return (
    <Modal show onHide={onClose} centered size="lg">
      <Modal.Header closeButton><Modal.Title className="h5">{credit.personName} · {credit.direction === 'Given' ? 'Money given' : 'Money borrowed'}</Modal.Title></Modal.Header>
      <Modal.Body>
        <div className="row g-2 mb-3 text-center">
          <div className="col-4"><div className="small text-muted">Amount</div><div className="fw-bold fs-5">{money(credit.amount)}</div></div>
          <div className="col-4"><div className="small text-muted">{credit.direction === 'Given' ? 'Received' : 'Repaid'}</div><div className="fw-bold fs-5 money-in">{money(credit.settledAmount)}</div></div>
          <div className="col-4"><div className="small text-muted">Outstanding</div><div className="fw-bold fs-5 money-out">{money(credit.outstanding)}</div></div>
        </div>
        <ProgressBar now={(credit.settledAmount / credit.amount) * 100} variant="success" className="mb-3" />
        <div className="small text-muted mb-3">Date {date(credit.transactionDate)} {credit.expectedReturnDate && `· return by ${date(credit.expectedReturnDate)}`} {credit.isOverdue && <Badge bg="danger">Overdue</Badge>} {credit.description && <div>{credit.description}</div>}</div>
        {credit.outstanding > 0 && <Card className="mb-3"><Card.Body className="row g-2 align-items-end">
          <div className="col-md-3"><Form.Label className="small">Amount</Form.Label><Form.Control size="sm" type="number" min="0.01" max={credit.outstanding} step="0.01" value={p.amount} onChange={(e) => setP({ ...p, amount: e.target.value })} /></div>
          <div className="col-md-3"><Form.Label className="small">Date</Form.Label><Form.Control size="sm" type="date" value={p.paymentDate} onChange={(e) => setP({ ...p, paymentDate: e.target.value })} /></div>
          <div className="col-md-3"><Form.Label className="small">Method</Form.Label><Form.Select size="sm" value={p.method} onChange={(e) => setP({ ...p, method: e.target.value })}>{PAYMENT_METHODS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</Form.Select></div>
          <div className="col-md-3 d-flex gap-1"><Button size="sm" disabled={!p.amount} onClick={() => void pay()}>Record</Button><Button size="sm" variant="outline-success" onClick={() => void pay(true)} title="Settle full amount"><FiCheckCircle /> Full</Button></div>
          <div className="col-12"><Form.Control size="sm" placeholder="Note (optional)" value={p.note} onChange={(e) => setP({ ...p, note: e.target.value })} /></div>
        </Card.Body></Card>}
        <h6 className="fw-bold small">Payment history</h6>
        {credit.payments.length === 0 ? <div className="small text-muted mb-3">No payments yet.</div> :
          <Table size="sm" className="small"><tbody>{credit.payments.map((x) => <tr key={x.id}><td>{date(x.paymentDate)}</td><td>{PAYMENT_METHODS.find((m) => m[0] === x.method)?.[1]}</td><td>{x.note}</td><td className="text-end money-in">{money(x.amount)}</td>
            <td className="text-end"><Button size="sm" variant="link" className="text-danger p-0" onClick={async () => { try { onChange((await api.delete<Credit>(`/api/udhar/${credit.id}/payments/${x.id}`)).data) } catch (e) { toast.error(e) } }}><FiTrash2 /></Button></td></tr>)}</tbody></Table>}
        <div className="d-flex justify-content-between align-items-center"><h6 className="fw-bold small mb-0">Attachments</h6>
          <label className="btn btn-sm btn-outline-primary mb-0"><FiPaperclip /> Attach<input type="file" hidden onChange={async (e) => { const f = e.target.files?.[0]; if (f) try { await uploadFile(`/api/udhar/${credit.id}/attachments`, f); onChange((await api.get<Credit>(`/api/udhar/${credit.id}`)).data) } catch (x) { toast.error(x) } }} /></label></div>
        <AttachmentList files={credit.attachments} urlFor={(f) => `/api/files/${f.id}`} />
      </Modal.Body>
      <Modal.Footer>
        <Button variant="link" className="text-danger me-auto" onClick={onDelete}><FiTrash2 /> Delete</Button>
        <Button variant="outline-secondary" onClick={() => void downloadFile(`/api/udhar/statement?${credit.contactId ? `contactId=${credit.contactId}` : `person=${encodeURIComponent(credit.personName)}`}&format=pdf`)}><FiDownload /> Statement</Button>
        <Button onClick={onEdit}>Edit</Button>
      </Modal.Footer>
    </Modal>
  )
}
