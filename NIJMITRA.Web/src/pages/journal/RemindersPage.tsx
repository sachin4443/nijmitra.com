import { useEffect, useState } from 'react'
import { Badge, Button, Card, Form, Modal } from 'react-bootstrap'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { FiAlertCircle, FiCalendar, FiCheckCircle, FiClock, FiEdit2, FiPlus, FiTrash2 } from 'react-icons/fi'
import { api } from '../../lib/api'
import { useAsync } from '../../lib/hooks'
import { dateTime, localInput } from '../../lib/format'
import { useToast } from '../../contexts/ToastContext'
import { EmptyState, Loading, PageHeader, useConfirm } from '../../components/ui'

interface Reminder { id: string; title: string; notes?: string; category: string; remindAtUtc: string; repeat: string; isCompleted: boolean }
interface Agenda { kind: string; id: string; title: string; at: string; detail?: string; link?: string }
const KIND: Record<string, [string, string]> = { meeting: ['Meeting', 'badge-soft'], appointment: ['Appointment', 'badge-soft'], reminder: ['Reminder', 'bg-soft-info'], bill: ['Bill due', 'bg-soft-warning'], 'bill-overdue': ['Bill overdue', 'bg-soft-danger'], 'udhar-collect': ['Collect Udhar', 'bg-soft-success'], 'udhar-repay': ['Repay Udhar', 'bg-soft-warning'] }

export default function RemindersPage() {
  const toast = useToast()
  const confirm = useConfirm()
  const nav = useNavigate()
  const [params, setParams] = useSearchParams()
  const [showDone, setShowDone] = useState(false)
  const list = useAsync(() => api.get<Reminder[]>('/api/calendar/reminders', { params: { includeCompleted: showDone } }).then((r) => r.data), [showDone])
  const upcoming = useAsync(() => api.get<Agenda[]>('/api/calendar/upcoming', { params: { days: 30 } }).then((r) => r.data), [])
  const [edit, setEdit] = useState<Partial<Reminder> | null>(null)
  useEffect(() => { if (params.get('new')) { setEdit({ category: 'General', repeat: 'None', remindAtUtc: new Date(Date.now() + 3600000).toISOString() }); params.delete('new'); setParams(params, { replace: true }) } }, [params, setParams])
  const reload = () => { void list.reload(); void upcoming.reload() }
  const save = async () => {
    if (!edit) return
    try {
      const body = { title: edit.title, notes: edit.notes, category: edit.category, remindAtUtc: edit.remindAtUtc, repeat: edit.repeat ?? 'None' }
      if (edit.id) await api.put(`/api/calendar/reminders/${edit.id}`, body); else await api.post('/api/calendar/reminders', body)
      toast.success('Reminder set'); setEdit(null); reload()
    } catch (x) { toast.error(x) }
  }
  return (
    <>
      <PageHeader title="Reminders" subtitle="समय पर याद — delivered even when the app is closed" icon={<FiClock />}>
        <Button variant="outline-secondary" onClick={() => nav('/calendar?new=1')}><FiCalendar /> New event</Button>
        <Button onClick={() => setEdit({ category: 'General', repeat: 'None', remindAtUtc: new Date(Date.now() + 3600000).toISOString() })}><FiPlus /> New reminder</Button>
      </PageHeader>
      <div className="row g-3">
        <div className="col-lg-6"><Card className="h-100"><Card.Header className="d-flex justify-content-between align-items-center">My reminders<Form.Check type="switch" id="sd" label="Show completed" checked={showDone} onChange={(e) => setShowDone(e.target.checked)} /></Card.Header><Card.Body>
          {list.loading ? <Loading /> : list.data?.length === 0 ? <EmptyState icon={<FiClock />} title="No reminders" text="Set reminders for calls, renewals, medicines — anything." /> : list.data?.map((r) => {
            const late = !r.isCompleted && new Date(r.remindAtUtc) < new Date()
            return (<div key={r.id} className="list-row">
              <Form.Check type="checkbox" checked={r.isCompleted} onChange={async () => { await api.post(`/api/calendar/reminders/${r.id}/complete`, null, { params: { value: !r.isCompleted } }); reload() }} aria-label="Done" />
              <div className={`flex-grow-1 min-w-0 ${r.isCompleted ? 'text-decoration-line-through text-muted' : ''}`}><div className="fw-medium">{r.title} {late && <FiAlertCircle className="text-danger" />}</div>
                <div className="small text-muted">{dateTime(r.remindAtUtc)} · {r.category}{r.repeat !== 'None' && ` · repeats ${r.repeat.toLowerCase()}`}</div>{r.notes && <div className="small">{r.notes}</div>}</div>
              <Button size="sm" variant="link" onClick={() => setEdit(r)}><FiEdit2 /></Button>
              <Button size="sm" variant="link" className="text-danger" onClick={async () => { if (await confirm({ title: 'Delete reminder?' })) { await api.delete(`/api/calendar/reminders/${r.id}`); reload() } }}><FiTrash2 /></Button>
            </div>)
          })}
        </Card.Body></Card></div>
        <div className="col-lg-6"><Card className="h-100"><Card.Header>Everything coming up (30 days)</Card.Header><Card.Body>
          {upcoming.loading ? <Loading /> : upcoming.data?.length === 0 ? <div className="small text-muted">All clear!</div> : upcoming.data?.map((a) => (
            <div key={a.kind + a.id + a.at} className="list-row clickable" onClick={() => a.link && nav(a.link)}>
              <Badge bg="" className={KIND[a.kind]?.[1] ?? 'badge-soft'}>{KIND[a.kind]?.[0] ?? a.kind}</Badge>
              <div className="flex-grow-1 min-w-0"><div className="truncate fw-medium">{a.title}</div><div className="small text-muted">{dateTime(a.at)} {a.detail && `· ${a.detail}`}</div></div>
              <FiCheckCircle className="text-muted" />
            </div>))}
        </Card.Body></Card></div>
      </div>
      <Modal show={!!edit} onHide={() => setEdit(null)} centered>
        <Modal.Header closeButton><Modal.Title className="h5">{edit?.id ? 'Edit reminder' : 'New reminder'}</Modal.Title></Modal.Header>
        <Modal.Body className="row g-3">
          <Form.Group className="col-12"><Form.Label>What should we remind you about?</Form.Label><Form.Control autoFocus value={edit?.title ?? ''} onChange={(e) => setEdit({ ...edit, title: e.target.value })} /></Form.Group>
          <Form.Group className="col-md-7"><Form.Label>When</Form.Label><Form.Control type="datetime-local" value={edit?.remindAtUtc ? localInput(edit.remindAtUtc) : ''} onChange={(e) => setEdit({ ...edit, remindAtUtc: new Date(e.target.value).toISOString() })} /></Form.Group>
          <Form.Group className="col-md-5"><Form.Label>Repeat</Form.Label><Form.Select value={edit?.repeat ?? 'None'} onChange={(e) => setEdit({ ...edit, repeat: e.target.value })}>{['None', 'Daily', 'Weekly', 'Monthly', 'Yearly'].map((r) => <option key={r}>{r}</option>)}</Form.Select></Form.Group>
          <Form.Group className="col-md-5"><Form.Label>Category</Form.Label><Form.Control list="rcats" value={edit?.category ?? ''} onChange={(e) => setEdit({ ...edit, category: e.target.value })} /><datalist id="rcats">{['General', 'Bills', 'Health', 'Work', 'Family', 'Renewal'].map((c) => <option key={c} value={c} />)}</datalist></Form.Group>
          <Form.Group className="col-md-7"><Form.Label>Notes</Form.Label><Form.Control value={edit?.notes ?? ''} onChange={(e) => setEdit({ ...edit, notes: e.target.value })} /></Form.Group>
        </Modal.Body>
        <Modal.Footer><Button variant="outline-secondary" onClick={() => setEdit(null)}>Cancel</Button><Button disabled={!edit?.title || !edit.remindAtUtc} onClick={() => void save()}>Save</Button></Modal.Footer>
      </Modal>
    </>
  )
}
