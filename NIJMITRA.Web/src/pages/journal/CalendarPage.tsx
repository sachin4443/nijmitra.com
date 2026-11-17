import { useCallback, useEffect, useRef, useState } from 'react'
import { Button, Card, Form, Modal, Spinner } from 'react-bootstrap'
import FullCalendar from '@fullcalendar/react'
import dayGridPlugin from '@fullcalendar/daygrid'
import timeGridPlugin from '@fullcalendar/timegrid'
import listPlugin from '@fullcalendar/list'
import interactionPlugin from '@fullcalendar/interaction'
import type { DatesSetArg, EventClickArg, EventInput } from '@fullcalendar/core'
import { useSearchParams } from 'react-router-dom'
import { FiCalendar, FiCheck, FiPaperclip, FiPlus, FiTrash2 } from 'react-icons/fi'
import { api, uploadFile } from '../../lib/api'
import { localInput } from '../../lib/format'
import { useToast } from '../../contexts/ToastContext'
import { AttachmentList, PageHeader, type FileInfo, useConfirm } from '../../components/ui'
import type { Contact } from '../people/ContactsPage'

const TYPES = ['Meeting', 'Appointment', 'Birthday', 'Task', 'BillPayment', 'FollowUp', 'Personal']
const COLORS: Record<string, string> = { Meeting: '#0B3D91', Appointment: '#1E88E5', Birthday: '#E6397E', Task: '#3AAA35', BillPayment: '#F47B20', FollowUp: '#7B3FBF', Personal: '#00A0A0' }
export const REMINDER_OPTIONS: [number, string][] = [[0, 'At event time'], [5, '5 minutes before'], [15, '15 minutes before'], [30, '30 minutes before'], [60, '1 hour before'], [1440, '1 day before']]
interface Occ { eventId: string; title: string; eventType: string; startUtc: string; endUtc: string; allDay: boolean; location?: string; priority: string; isCompleted: boolean; isRecurring: boolean }
interface Ev { id?: string; title: string; description?: string; eventType: string; startUtc: string; endUtc: string; allDay: boolean; location?: string; priority: string; reminderOffsets: number[]; recurrence: string; recurrenceUntilUtc?: string | null; isCompleted?: boolean; contactId?: string | null; attachments?: FileInfo[] }

export default function CalendarPage() {
  const toast = useToast()
  const [params, setParams] = useSearchParams()
  const [events, setEvents] = useState<EventInput[]>([])
  const range = useRef<{ start: Date; end: Date } | null>(null)
  const [edit, setEdit] = useState<Ev | null>(null)
  const load = useCallback(async () => {
    if (!range.current) return
    try {
      const r = await api.get<Occ[]>('/api/calendar/events', { params: { from: range.current.start.toISOString(), to: range.current.end.toISOString() } })
      setEvents(r.data.map((o) => ({ id: o.eventId + o.startUtc, title: (o.isCompleted ? '✓ ' : '') + o.title, start: o.startUtc, end: o.endUtc, allDay: o.allDay, backgroundColor: COLORS[o.eventType], borderColor: COLORS[o.eventType], extendedProps: { eventId: o.eventId, location: o.location }, classNames: o.isCompleted ? ['opacity-50'] : [] })))
    } catch (e) { toast.error(e) }
  }, [toast])
  const blank = (start = new Date()): Ev => { const s = new Date(start); if (s.getHours() === 0) s.setHours(10); const e = new Date(s.getTime() + 3600000); return { title: '', eventType: 'Meeting', startUtc: s.toISOString(), endUtc: e.toISOString(), allDay: false, priority: 'Normal', reminderOffsets: [15], recurrence: 'None' } }
  useEffect(() => {
    if (params.get('new')) { setEdit(blank()); params.delete('new'); setParams(params, { replace: true }) }
    const id = params.get('event')
    if (id) { api.get<Ev>(`/api/calendar/events/${id}`).then((r) => setEdit(r.data)).catch(() => {}); params.delete('event'); setParams(params, { replace: true }) }
  }, [params, setParams])
  return (
    <>
      <PageHeader title="Calendar" subtitle="Meetings, appointments, birthdays, tasks, bills and follow-ups" icon={<FiCalendar />}><Button onClick={() => setEdit(blank())}><FiPlus /> New event</Button></PageHeader>
      <Card><Card.Body>
        <FullCalendar plugins={[dayGridPlugin, timeGridPlugin, listPlugin, interactionPlugin]} initialView="dayGridMonth" height="auto"
          headerToolbar={{ left: 'prev,next today', center: 'title', right: 'timeGridDay,timeGridWeek,dayGridMonth,listMonth' }}
          buttonText={{ day: 'Day', week: 'Week', month: 'Month', list: 'Agenda', today: 'Today' }} nowIndicator dayMaxEvents={3} events={events}
          datesSet={(a: DatesSetArg) => { range.current = { start: a.start, end: a.end }; void load() }}
          dateClick={(a) => setEdit(blank(a.date))}
          eventClick={async (a: EventClickArg) => { const r = await api.get<Ev>(`/api/calendar/events/${a.event.extendedProps.eventId}`); setEdit(r.data) }} />
      </Card.Body></Card>
      {edit && <EventModal value={edit} onClose={(changed) => { setEdit(null); if (changed) void load() }} />}
    </>
  )
}

function EventModal({ value, onClose }: { value: Ev; onClose: (changed: boolean) => void }) {
  const toast = useToast()
  const confirm = useConfirm()
  const [e, setE] = useState<Ev>(value)
  const [contacts, setContacts] = useState<Contact[]>([])
  const [busy, setBusy] = useState(false)
  const [custom, setCustom] = useState('')
  useEffect(() => { api.get('/api/contacts', { params: { pageSize: 200 } }).then((r) => setContacts(r.data.items)).catch(() => {}) }, [])
  const save = async () => {
    setBusy(true)
    try {
      const body = { ...e, contactId: e.contactId || null, recurrenceUntilUtc: e.recurrenceUntilUtc || null }
      if (e.id) await api.put(`/api/calendar/events/${e.id}`, body); else await api.post('/api/calendar/events', body)
      toast.success('Event saved — reminders scheduled'); onClose(true)
    } catch (x) { toast.error(x) } finally { setBusy(false) }
  }
  const toggleOffset = (m: number) => setE({ ...e, reminderOffsets: e.reminderOffsets.includes(m) ? e.reminderOffsets.filter((x) => x !== m) : [...e.reminderOffsets, m] })
  return (
    <Modal show onHide={() => onClose(false)} size="lg" centered>
      <Modal.Header closeButton><Modal.Title className="h5">{e.id ? 'Edit event' : 'New event'}</Modal.Title></Modal.Header>
      <Modal.Body className="row g-3">
        <Form.Group className="col-md-8"><Form.Label>Title</Form.Label><Form.Control value={e.title} onChange={(x) => setE({ ...e, title: x.target.value })} autoFocus /></Form.Group>
        <Form.Group className="col-md-4"><Form.Label>Type</Form.Label><Form.Select value={e.eventType} onChange={(x) => setE({ ...e, eventType: x.target.value })}>{TYPES.map((t) => <option key={t} value={t}>{t.replace(/([a-z])([A-Z])/g, '$1 $2')}</option>)}</Form.Select></Form.Group>
        <Form.Group className="col-md-5"><Form.Label>Start</Form.Label><Form.Control type="datetime-local" value={localInput(e.startUtc)} onChange={(x) => { const s = new Date(x.target.value); const dur = new Date(e.endUtc).getTime() - new Date(e.startUtc).getTime(); setE({ ...e, startUtc: s.toISOString(), endUtc: new Date(s.getTime() + Math.max(dur, 0)).toISOString() }) }} /></Form.Group>
        <Form.Group className="col-md-5"><Form.Label>End</Form.Label><Form.Control type="datetime-local" value={localInput(e.endUtc)} onChange={(x) => setE({ ...e, endUtc: new Date(x.target.value).toISOString() })} /></Form.Group>
        <Form.Check className="col-md-2 align-self-end mb-2" type="switch" id="allday" label="All day" checked={e.allDay} onChange={(x) => setE({ ...e, allDay: x.target.checked })} />
        <Form.Group className="col-md-6"><Form.Label>Location</Form.Label><Form.Control value={e.location ?? ''} onChange={(x) => setE({ ...e, location: x.target.value })} /></Form.Group>
        <Form.Group className="col-md-3"><Form.Label>Priority</Form.Label><Form.Select value={e.priority} onChange={(x) => setE({ ...e, priority: x.target.value })}>{['Low', 'Normal', 'High', 'Urgent'].map((p) => <option key={p}>{p}</option>)}</Form.Select></Form.Group>
        <Form.Group className="col-md-3"><Form.Label>With contact</Form.Label><Form.Select value={e.contactId ?? ''} onChange={(x) => setE({ ...e, contactId: x.target.value })}><option value="">—</option>{contacts.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</Form.Select></Form.Group>
        <Form.Group className="col-md-6"><Form.Label>Repeat</Form.Label><Form.Select value={e.recurrence} onChange={(x) => setE({ ...e, recurrence: x.target.value })}>{['None', 'Daily', 'Weekly', 'Monthly', 'Yearly'].map((r) => <option key={r}>{r}</option>)}</Form.Select></Form.Group>
        {e.recurrence !== 'None' && <Form.Group className="col-md-6"><Form.Label>Repeat until (optional)</Form.Label><Form.Control type="date" value={e.recurrenceUntilUtc?.slice(0, 10) ?? ''} onChange={(x) => setE({ ...e, recurrenceUntilUtc: x.target.value ? new Date(x.target.value + 'T23:59:59').toISOString() : null })} /></Form.Group>}
        <div className="col-12"><Form.Label>Remind me</Form.Label><div className="d-flex flex-wrap gap-2">
          {REMINDER_OPTIONS.map(([m, l]) => <Form.Check key={m} inline type="checkbox" id={`rm${m}`} label={l} checked={e.reminderOffsets.includes(m)} onChange={() => toggleOffset(m)} />)}
          {e.reminderOffsets.filter((m) => !REMINDER_OPTIONS.some(([x]) => x === m)).map((m) => <span key={m} className="tag">{m} min before <span role="button" onClick={() => toggleOffset(m)}>✕</span></span>)}
          <div className="d-flex gap-1"><Form.Control size="sm" type="number" min={1} placeholder="Custom min" value={custom} onChange={(x) => setCustom(x.target.value)} style={{ width: 120 }} /><Button size="sm" variant="outline-secondary" disabled={!custom} onClick={() => { toggleOffset(Number(custom)); setCustom('') }}>Add</Button></div>
        </div></div>
        <Form.Group className="col-12"><Form.Label>Description</Form.Label><Form.Control as="textarea" rows={3} value={e.description ?? ''} onChange={(x) => setE({ ...e, description: x.target.value })} /></Form.Group>
        {e.id && <div className="col-12"><div className="d-flex justify-content-between"><span className="small fw-semibold">Attachments</span>
          <label className="btn btn-sm btn-outline-primary mb-0"><FiPaperclip /> Attach<input type="file" hidden onChange={async (x) => { const f = x.target.files?.[0]; if (f) try { const r = await uploadFile<FileInfo>(`/api/calendar/events/${e.id}/attachments`, f); setE({ ...e, attachments: [...(e.attachments ?? []), r] }) } catch (err) { toast.error(err) } }} /></label></div>
          <AttachmentList files={e.attachments ?? []} urlFor={(f) => `/api/files/${f.id}`} /></div>}
      </Modal.Body>
      <Modal.Footer>
        {e.id && <Button variant="link" className="text-danger me-auto" onClick={async () => { if (await confirm({ title: 'Delete this event?', body: e.recurrence !== 'None' ? 'All occurrences of this repeating event will be deleted.' : undefined })) { await api.delete(`/api/calendar/events/${e.id}`); onClose(true) } }}><FiTrash2 /> Delete</Button>}
        {e.id && <Button variant="outline-success" onClick={async () => { await api.post(`/api/calendar/events/${e.id}/complete`, null, { params: { value: !e.isCompleted } }); onClose(true) }}><FiCheck /> {e.isCompleted ? 'Mark not done' : 'Mark done'}</Button>}
        <Button variant="outline-secondary" onClick={() => onClose(false)}>Cancel</Button>
        <Button disabled={busy || !e.title.trim()} onClick={() => void save()}>{busy ? <Spinner size="sm" /> : 'Save'}</Button>
      </Modal.Footer>
    </Modal>
  )
}
