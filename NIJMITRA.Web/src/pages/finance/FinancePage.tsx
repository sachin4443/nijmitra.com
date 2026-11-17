import { useEffect, useMemo, useState } from 'react'
import { Badge, Button, Card, Form, Modal, Nav, ProgressBar, Spinner, Table } from 'react-bootstrap'
import { NavLink, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { Bar, BarChart, CartesianGrid, Cell, Legend, Line, LineChart, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { FiCheck, FiCreditCard, FiEdit2, FiPaperclip, FiPlus, FiRepeat, FiSkipForward, FiTrash2 } from 'react-icons/fi'
import { api, uploadFile } from '../../lib/api'
import { useAsync, useDebounced } from '../../lib/hooks'
import { date, money, today } from '../../lib/format'
import { useToast } from '../../contexts/ToastContext'
import { EmptyState, ExportMenu, Loading, PageHeader, Pager, PreviewModal, StatCard, StatusBadge, useConfirm } from '../../components/ui'

export interface Category { id: string; name: string; type: 'Expense' | 'Income'; icon?: string; color?: string; isDefault: boolean }
interface Expense { id: string; name: string; amount: number; categoryId: string; categoryName?: string; categoryColor?: string; paymentMethod: string; date: string; description?: string; notes?: string; receiptFileId?: string; contactId?: string }
interface Income { id: string; title: string; source: string; amount: number; date: string; description?: string; notes?: string }
export const PAYMENT_METHODS = [['Cash', 'Cash'], ['Upi', 'UPI'], ['DebitCard', 'Debit card'], ['CreditCard', 'Credit card'], ['NetBanking', 'Net banking'], ['Wallet', 'Wallet'], ['Cheque', 'Cheque'], ['Other', 'Other']]
const SOURCES = ['Salary', 'Business Income', 'Freelancing', 'Interest', 'Other Income']
const PALETTE = ['#0B3D91', '#F47B20', '#3AAA35', '#7B3FBF', '#E6397E', '#F9A825', '#1E88E5', '#00A0A0', '#C0392B', '#8E44AD']
const pmLabel = (v: string) => PAYMENT_METHODS.find((p) => p[0] === v)?.[1] ?? v

export function useCategories(type: 'Expense' | 'Income' = 'Expense') {
  return useAsync(() => api.get<Category[]>('/api/finance/categories', { params: { type } }).then((r) => r.data), [type])
}

export default function FinancePage() {
  const { section = 'expenses' } = useParams()
  const tabs = [['expenses', 'Expenses'], ['income', 'Income'], ['budget', 'Budget'], ['recurring', 'Recurring bills'], ['reports', 'Reports'], ['categories', 'Categories']]
  return (
    <>
      <PageHeader title="Finance" subtitle="Expenses, income, budgets and reports" icon={<FiCreditCard />} />
      <Nav variant="pills" className="mb-3 gap-1 flex-nowrap overflow-auto">{tabs.map(([k, l]) => <Nav.Item key={k}><Nav.Link as={NavLink} to={`/finance/${k}`} className="text-nowrap">{l}</Nav.Link></Nav.Item>)}</Nav>
      {section === 'expenses' && <Expenses />}
      {section === 'income' && <IncomeView />}
      {section === 'budget' && <BudgetView />}
      {section === 'recurring' && <RecurringView />}
      {section === 'reports' && <ReportsView />}
      {section === 'categories' && <CategoriesView />}
    </>
  )
}

// ------------------------------------------------------------------ expenses
function Expenses() {
  const toast = useToast()
  const confirm = useConfirm()
  const [params, setParams] = useSearchParams()
  const cats = useCategories()
  const [f, setF] = useState({ from: '', to: '', categoryId: '', paymentMethod: '', search: '' })
  const [page, setPage] = useState(1)
  const search = useDebounced(f.search)
  const query = { from: f.from || undefined, to: f.to || undefined, categoryId: f.categoryId || undefined, paymentMethod: f.paymentMethod || undefined, search: search || undefined }
  const list = useAsync(() => api.get('/api/finance/expenses', { params: { ...query, page, pageSize: 20 } }).then((r) => r.data as { page: { items: Expense[]; totalPages: number; totalCount: number }; total: number }), [f.from, f.to, f.categoryId, f.paymentMethod, search, page])
  const [edit, setEdit] = useState<Partial<Expense> | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  useEffect(() => { if (params.get('new')) { setEdit({}); params.delete('new'); setParams(params, { replace: true }) } }, [params, setParams])
  const qs = new URLSearchParams(Object.entries(query).filter(([, v]) => v) as [string, string][]).toString()
  return (
    <>
      <Card className="mb-3"><Card.Body className="row g-2 align-items-end">
        <div className="col-md-3"><Form.Control placeholder="Search expenses…" value={f.search} onChange={(e) => { setF({ ...f, search: e.target.value }); setPage(1) }} /></div>
        <div className="col-6 col-md-2"><Form.Control type="date" value={f.from} onChange={(e) => setF({ ...f, from: e.target.value })} aria-label="From" /></div>
        <div className="col-6 col-md-2"><Form.Control type="date" value={f.to} onChange={(e) => setF({ ...f, to: e.target.value })} aria-label="To" /></div>
        <div className="col-6 col-md-2"><Form.Select value={f.categoryId} onChange={(e) => setF({ ...f, categoryId: e.target.value })}><option value="">All categories</option>{cats.data?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</Form.Select></div>
        <div className="col-6 col-md-1"><Form.Select value={f.paymentMethod} onChange={(e) => setF({ ...f, paymentMethod: e.target.value })}><option value="">Any</option>{PAYMENT_METHODS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</Form.Select></div>
        <div className="col-md-2 d-flex gap-2 justify-content-end"><ExportMenu url={(fmt) => `/api/finance/expenses/export?format=${fmt}&${qs}`} /><Button size="sm" onClick={() => setEdit({})}><FiPlus /> Add</Button></div>
      </Card.Body></Card>
      <Card>
        <Card.Header className="d-flex justify-content-between"><span>{list.data?.page.totalCount ?? 0} expense(s)</span><span>Total: <strong className="money-out">{money(list.data?.total ?? 0)}</strong></span></Card.Header>
        {list.loading && !list.data ? <Loading /> : list.data?.page.items.length === 0 ? <EmptyState title="No expenses found" text="Record your first expense to start tracking." action={<Button onClick={() => setEdit({})}><FiPlus /> Add expense</Button>} /> : (
          <Table responsive hover className="mb-0 align-middle">
            <thead><tr><th>Date</th><th>Expense</th><th>Category</th><th>Payment</th><th className="text-end">Amount</th><th /></tr></thead>
            <tbody>{list.data?.page.items.map((e) => (
              <tr key={e.id}>
                <td className="text-nowrap">{date(e.date)}</td>
                <td><div className="fw-medium">{e.name}</div>{e.description && <div className="small text-muted">{e.description}</div>}</td>
                <td><Badge bg="" style={{ background: (e.categoryColor ?? '#64748B') + '22', color: e.categoryColor ?? undefined }}>{e.categoryName}</Badge></td>
                <td className="small">{pmLabel(e.paymentMethod)}</td>
                <td className="text-end fw-semibold money-out">{money(e.amount)}</td>
                <td className="text-end text-nowrap">
                  {e.receiptFileId && <Button size="sm" variant="link" title="Receipt" onClick={() => setPreview(e.receiptFileId!)}><FiPaperclip /></Button>}
                  <Button size="sm" variant="link" onClick={() => setEdit(e)} aria-label="Edit"><FiEdit2 /></Button>
                  <Button size="sm" variant="link" className="text-danger" aria-label="Delete" onClick={async () => { if (await confirm({ title: `Delete "${e.name}"?` })) { await api.delete(`/api/finance/expenses/${e.id}`).catch(toast.error); void list.reload() } }}><FiTrash2 /></Button>
                </td>
              </tr>))}</tbody>
          </Table>)}
        <Card.Footer><Pager page={page} totalPages={list.data?.page.totalPages ?? 1} onChange={setPage} /></Card.Footer>
      </Card>
      {edit && <ExpenseModal value={edit} categories={cats.data ?? []} onClose={(saved) => { setEdit(null); if (saved) void list.reload() }} />}
      <PreviewModal url={preview ? `/api/files/${preview}?inline=true` : null} name="Receipt" contentType={undefined} onHide={() => setPreview(null)} />
    </>
  )
}

function ExpenseModal({ value, categories, onClose }: { value: Partial<Expense>; categories: Category[]; onClose: (saved: boolean) => void }) {
  const toast = useToast()
  const [f, setF] = useState({ name: value.name ?? '', amount: value.amount?.toString() ?? '', categoryId: value.categoryId ?? categories[0]?.id ?? '', paymentMethod: value.paymentMethod ?? 'Upi',
    date: value.date ?? today(), description: value.description ?? '', notes: value.notes ?? '', receiptFileId: value.receiptFileId ?? '' })
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<Record<string, string>>({})
  // Categories may arrive after the form opens (e.g. via Quick Add) — default to the first one.
  useEffect(() => { if (!f.categoryId && categories[0]) setF((x) => ({ ...x, categoryId: categories[0]!.id })) }, [categories, f.categoryId])
  const save = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setErr({})
    try {
      const body = { ...f, amount: Number(f.amount), receiptFileId: f.receiptFileId || null }
      if (value.id) await api.put(`/api/finance/expenses/${value.id}`, body); else await api.post('/api/finance/expenses', body)
      toast.success('Expense saved'); onClose(true)
    } catch (x) { const { apiError } = await import('../../lib/api'); const a = apiError(x); setErr(a.fields); toast.push('error', a.message) } finally { setBusy(false) }
  }
  const upload = async (file?: File) => {
    if (!file) return
    try { const r = await uploadFile<{ id: string }>('/api/uploads?purpose=receipt', file); setF({ ...f, receiptFileId: r.id }); toast.success('Receipt attached') } catch (x) { toast.error(x) }
  }
  return (
    <Modal show onHide={() => onClose(false)} centered>
      <Form onSubmit={save}>
        <Modal.Header closeButton><Modal.Title className="h5">{value.id ? 'Edit expense' : 'Add expense'}</Modal.Title></Modal.Header>
        <Modal.Body className="row g-3">
          <Form.Group className="col-12"><Form.Label>Expense name</Form.Label><Form.Control required value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} isInvalid={!!err.name} placeholder="e.g. Electricity bill" /><Form.Control.Feedback type="invalid">{err.name}</Form.Control.Feedback></Form.Group>
          <Form.Group className="col-6"><Form.Label>Amount (₹)</Form.Label><Form.Control required type="number" step="0.01" min="0.01" value={f.amount} onChange={(e) => setF({ ...f, amount: e.target.value })} isInvalid={!!err.amount} /></Form.Group>
          <Form.Group className="col-6"><Form.Label>Date</Form.Label><Form.Control required type="date" value={f.date} onChange={(e) => setF({ ...f, date: e.target.value })} /></Form.Group>
          <Form.Group className="col-6"><Form.Label>Category</Form.Label><Form.Select value={f.categoryId} onChange={(e) => setF({ ...f, categoryId: e.target.value })}>{categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</Form.Select></Form.Group>
          <Form.Group className="col-6"><Form.Label>Payment method</Form.Label><Form.Select value={f.paymentMethod} onChange={(e) => setF({ ...f, paymentMethod: e.target.value })}>{PAYMENT_METHODS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</Form.Select></Form.Group>
          <Form.Group className="col-12"><Form.Label>Description</Form.Label><Form.Control value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} /></Form.Group>
          <Form.Group className="col-12"><Form.Label>Notes</Form.Label><Form.Control as="textarea" rows={2} value={f.notes} onChange={(e) => setF({ ...f, notes: e.target.value })} /></Form.Group>
          <Form.Group className="col-12"><Form.Label>Receipt (image or PDF)</Form.Label>
            <div className="d-flex gap-2 align-items-center"><Form.Control type="file" accept=".pdf,.jpg,.jpeg,.png,.webp" onChange={(e) => void upload((e.target as HTMLInputElement).files?.[0])} />
              {f.receiptFileId && <Badge bg="" className="bg-soft-success">Attached</Badge>}</div></Form.Group>
        </Modal.Body>
        <Modal.Footer><Button variant="outline-secondary" onClick={() => onClose(false)}>Cancel</Button><Button type="submit" disabled={busy}>{busy ? <Spinner size="sm" /> : 'Save'}</Button></Modal.Footer>
      </Form>
    </Modal>
  )
}

// ------------------------------------------------------------------ income
function IncomeView() {
  const toast = useToast()
  const confirm = useConfirm()
  const [params, setParams] = useSearchParams()
  const [page, setPage] = useState(1)
  const [source, setSource] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const list = useAsync(() => api.get('/api/finance/income', { params: { page, pageSize: 20, source: source || undefined, from: from || undefined, to: to || undefined } }).then((r) => r.data as { items: Income[]; totalPages: number; totalCount: number }), [page, source, from, to])
  const [edit, setEdit] = useState<Partial<Income> | null>(null)
  useEffect(() => { if (params.get('new')) { setEdit({}); params.delete('new'); setParams(params, { replace: true }) } }, [params, setParams])
  const save = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!edit) return
    try {
      const body = { title: edit.title, source: edit.source ?? 'Salary', amount: Number(edit.amount), date: edit.date ?? today(), description: edit.description, notes: edit.notes }
      if (edit.id) await api.put(`/api/finance/income/${edit.id}`, body); else await api.post('/api/finance/income', body)
      toast.success('Income saved'); setEdit(null); void list.reload()
    } catch (x) { toast.error(x) }
  }
  return (
    <>
      <Card className="mb-3"><Card.Body className="row g-2 align-items-end">
        <div className="col-md-3"><Form.Select value={source} onChange={(e) => setSource(e.target.value)}><option value="">All sources</option>{SOURCES.map((s) => <option key={s}>{s}</option>)}</Form.Select></div>
        <div className="col-6 col-md-2"><Form.Control type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></div>
        <div className="col-6 col-md-2"><Form.Control type="date" value={to} onChange={(e) => setTo(e.target.value)} /></div>
        <div className="col-md-5 d-flex gap-2 justify-content-end"><ExportMenu url={(fmt) => `/api/finance/income/export?format=${fmt}`} /><Button size="sm" onClick={() => setEdit({ source: 'Salary', date: today() })}><FiPlus /> Add income</Button></div>
      </Card.Body></Card>
      <Card>
        {list.loading && !list.data ? <Loading /> : list.data?.items.length === 0 ? <EmptyState title="No income recorded" action={<Button onClick={() => setEdit({ source: 'Salary', date: today() })}><FiPlus /> Add income</Button>} /> : (
          <Table responsive hover className="mb-0 align-middle"><thead><tr><th>Date</th><th>Title</th><th>Source</th><th className="text-end">Amount</th><th /></tr></thead>
            <tbody>{list.data?.items.map((i) => <tr key={i.id}><td>{date(i.date)}</td><td><div className="fw-medium">{i.title}</div>{i.description && <div className="small text-muted">{i.description}</div>}</td><td><Badge bg="" className="bg-soft-success">{i.source}</Badge></td>
              <td className="text-end fw-semibold money-in">{money(i.amount)}</td>
              <td className="text-end text-nowrap"><Button size="sm" variant="link" onClick={() => setEdit(i)}><FiEdit2 /></Button>
                <Button size="sm" variant="link" className="text-danger" onClick={async () => { if (await confirm({ title: 'Delete this income record?' })) { await api.delete(`/api/finance/income/${i.id}`); void list.reload() } }}><FiTrash2 /></Button></td></tr>)}</tbody></Table>)}
        <Card.Footer><Pager page={page} totalPages={list.data?.totalPages ?? 1} onChange={setPage} /></Card.Footer>
      </Card>
      <Modal show={!!edit} onHide={() => setEdit(null)} centered><Form onSubmit={save}>
        <Modal.Header closeButton><Modal.Title className="h5">{edit?.id ? 'Edit income' : 'Add income'}</Modal.Title></Modal.Header>
        <Modal.Body className="row g-3">
          <Form.Group className="col-12"><Form.Label>Title</Form.Label><Form.Control required value={edit?.title ?? ''} onChange={(e) => setEdit({ ...edit, title: e.target.value })} placeholder="e.g. November salary" /></Form.Group>
          <Form.Group className="col-6"><Form.Label>Amount (₹)</Form.Label><Form.Control required type="number" step="0.01" min="0.01" value={edit?.amount ?? ''} onChange={(e) => setEdit({ ...edit, amount: e.target.value as unknown as number })} /></Form.Group>
          <Form.Group className="col-6"><Form.Label>Date</Form.Label><Form.Control type="date" value={edit?.date ?? today()} onChange={(e) => setEdit({ ...edit, date: e.target.value })} /></Form.Group>
          <Form.Group className="col-12"><Form.Label>Source</Form.Label><Form.Control list="sources" value={edit?.source ?? ''} onChange={(e) => setEdit({ ...edit, source: e.target.value })} /><datalist id="sources">{SOURCES.map((s) => <option key={s} value={s} />)}</datalist></Form.Group>
          <Form.Group className="col-12"><Form.Label>Description</Form.Label><Form.Control value={edit?.description ?? ''} onChange={(e) => setEdit({ ...edit, description: e.target.value })} /></Form.Group>
        </Modal.Body>
        <Modal.Footer><Button variant="outline-secondary" onClick={() => setEdit(null)}>Cancel</Button><Button type="submit">Save</Button></Modal.Footer>
      </Form></Modal>
    </>
  )
}

// ------------------------------------------------------------------ budget
interface BudgetLine { categoryId: string; categoryName: string; color?: string; budget: number; spent: number; remaining: number; utilizationPercent: number; overspent: boolean }
interface Budget { id?: string; year: number; month: number; totalBudget: number; totalSpent: number; remaining: number; utilizationPercent: number; alertThresholdPercent: number; overspent: boolean; categories: BudgetLine[] }

function BudgetView() {
  const toast = useToast()
  const now = new Date()
  const [ym, setYm] = useState(`${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`)
  const [y, m] = ym.split('-').map(Number) as [number, number]
  const cats = useCategories()
  const b = useAsync(() => api.get<Budget>(`/api/finance/budget/${y}/${m}`).then((r) => r.data), [ym])
  const cmp = useAsync(() => api.get('/api/finance/budget/comparison', { params: { months: 6 } }).then((r) => r.data as { year: number; month: number; budget: number; spent: number; income: number }[]), [])
  const [edit, setEdit] = useState(false)
  const [total, setTotal] = useState('')
  const [threshold, setThreshold] = useState(90)
  const [lines, setLines] = useState<Record<string, string>>({})
  const open = () => {
    setTotal(String(b.data?.totalBudget || '')); setThreshold(b.data?.alertThresholdPercent ?? 90)
    setLines(Object.fromEntries((b.data?.categories ?? []).filter((c) => c.budget > 0).map((c) => [c.categoryId, String(c.budget)]))); setEdit(true)
  }
  const save = async () => {
    try {
      await api.put('/api/finance/budget', { year: y, month: m, totalAmount: Number(total || 0), alertThresholdPercent: threshold, categories: Object.entries(lines).filter(([, v]) => Number(v) > 0).map(([categoryId, v]) => ({ categoryId, amount: Number(v) })) })
      toast.success('Budget saved'); setEdit(false); void b.reload(); void cmp.reload()
    } catch (x) { toast.error(x) }
  }
  const d = b.data
  return (
    <>
      <div className="d-flex flex-wrap gap-2 justify-content-between mb-3">
        <Form.Control type="month" value={ym} onChange={(e) => setYm(e.target.value)} style={{ maxWidth: 200 }} />
        <Button onClick={open}><FiEdit2 /> {d?.id ? 'Edit budget' : 'Set budget'}</Button>
      </div>
      {!d ? <Loading /> : (
        <>
          <div className="row g-3 mb-3">
            <div className="col-6 col-lg-3"><StatCard label="Monthly budget" value={money(d.totalBudget)} icon={<FiCreditCard />} /></div>
            <div className="col-6 col-lg-3"><StatCard label="Actual spending" value={money(d.totalSpent)} icon={<FiCreditCard />} tone="danger" /></div>
            <div className="col-6 col-lg-3"><StatCard label="Remaining" value={money(d.remaining)} icon={<FiCreditCard />} tone={d.remaining < 0 ? 'danger' : 'success'} /></div>
            <div className="col-6 col-lg-3"><StatCard label="Utilization" value={`${d.utilizationPercent}%`} icon={<FiCreditCard />} tone={d.overspent ? 'danger' : d.utilizationPercent >= d.alertThresholdPercent ? 'warning' : 'info'} hint={d.overspent ? 'Over budget!' : `Alert at ${d.alertThresholdPercent}%`} /></div>
          </div>
          {d.totalBudget > 0 && <ProgressBar className="mb-3" style={{ height: 12 }} now={Math.min(100, d.utilizationPercent)} variant={d.overspent ? 'danger' : d.utilizationPercent >= d.alertThresholdPercent ? 'warning' : 'success'} />}
          <div className="row g-3">
            <div className="col-lg-7"><Card className="h-100"><Card.Header>Budget vs actual by category</Card.Header><Card.Body>
              {d.categories.length === 0 ? <div className="small text-muted">No spending or category budgets for this month.</div> : d.categories.map((c) => (
                <div key={c.categoryId} className="mb-3">
                  <div className="d-flex justify-content-between small"><span className="fw-medium">{c.categoryName}</span><span>{money(c.spent)} {c.budget > 0 && <>/ {money(c.budget)}</>} {c.overspent && <Badge bg="danger">Over</Badge>}</span></div>
                  <ProgressBar style={{ height: 8 }} now={c.budget > 0 ? Math.min(100, c.utilizationPercent) : 100} variant={c.overspent ? 'danger' : c.budget === 0 ? 'secondary' : 'primary'} />
                </div>))}
            </Card.Body></Card></div>
            <div className="col-lg-5"><Card className="h-100"><Card.Header>Monthly comparison</Card.Header><Card.Body style={{ height: 300 }}>
              <ResponsiveContainer><LineChart data={(cmp.data ?? []).map((x) => ({ ...x, label: new Date(x.year, x.month - 1).toLocaleString('en-IN', { month: 'short' }) }))}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--nm-border)" /><XAxis dataKey="label" stroke="var(--nm-muted)" /><YAxis stroke="var(--nm-muted)" /><Tooltip formatter={(v) => money(Number(v))} /><Legend />
                <Line dataKey="budget" name="Budget" stroke="#0B3D91" strokeWidth={2} /><Line dataKey="spent" name="Spent" stroke="#F47B20" strokeWidth={2} /><Line dataKey="income" name="Income" stroke="#3AAA35" strokeWidth={2} /></LineChart></ResponsiveContainer>
            </Card.Body></Card></div>
          </div>
        </>)}
      <Modal show={edit} onHide={() => setEdit(false)} centered size="lg">
        <Modal.Header closeButton><Modal.Title className="h5">Budget for {new Date(y, m - 1).toLocaleString('en-IN', { month: 'long', year: 'numeric' })}</Modal.Title></Modal.Header>
        <Modal.Body>
          <div className="row g-3 mb-3">
            <Form.Group className="col-md-6"><Form.Label>Total monthly budget (₹)</Form.Label><Form.Control type="number" min="0" value={total} onChange={(e) => setTotal(e.target.value)} /></Form.Group>
            <Form.Group className="col-md-6"><Form.Label>Alert me at {threshold}% usage</Form.Label><Form.Range min={50} max={100} value={threshold} onChange={(e) => setThreshold(Number(e.target.value))} /></Form.Group>
          </div>
          <h6 className="fw-bold">Category budgets <span className="small text-muted fw-normal">(optional)</span></h6>
          <div className="row g-2">{cats.data?.map((c) => <div className="col-md-6" key={c.id}><div className="input-group input-group-sm"><span className="input-group-text" style={{ minWidth: 120 }}>{c.name}</span>
            <Form.Control type="number" min="0" value={lines[c.id] ?? ''} onChange={(e) => setLines({ ...lines, [c.id]: e.target.value })} placeholder="₹" /></div></div>)}</div>
        </Modal.Body>
        <Modal.Footer><Button variant="outline-secondary" onClick={() => setEdit(false)}>Cancel</Button><Button onClick={() => void save()}>Save budget</Button></Modal.Footer>
      </Modal>
    </>
  )
}

// ------------------------------------------------------------------ recurring
interface Recurring { id: string; name: string; amount: number; categoryId: string; categoryName?: string; paymentMethod: string; frequency: string; customIntervalDays?: number; startDate: string; endDate?: string; reminderDaysBefore: number; isActive: boolean; notes?: string; nextDueDate?: string; overdueCount: number }
interface Occurrence { id: string; recurringExpenseId: string; name: string; amount: number; dueDate: string; status: string }
const FREQ = ['Daily', 'Weekly', 'Monthly', 'Quarterly', 'Yearly', 'Custom']

function RecurringView() {
  const toast = useToast()
  const confirm = useConfirm()
  const cats = useCategories()
  const rec = useAsync(() => api.get<Recurring[]>('/api/finance/recurring').then((r) => r.data), [])
  const occ = useAsync(() => api.get<Occurrence[]>('/api/finance/recurring/occurrences').then((r) => r.data.filter((o) => o.status === 'Pending' || o.status === 'Overdue')), [])
  const [edit, setEdit] = useState<Partial<Recurring> | null>(null)
  const reload = () => { void rec.reload(); void occ.reload() }
  const save = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!edit) return
    try {
      const body = { name: edit.name, amount: Number(edit.amount), categoryId: edit.categoryId ?? cats.data?.[0]?.id, paymentMethod: edit.paymentMethod ?? 'Upi', frequency: edit.frequency ?? 'Monthly',
        customIntervalDays: edit.frequency === 'Custom' ? Number(edit.customIntervalDays) : null, startDate: edit.startDate ?? today(), endDate: edit.endDate || null, reminderDaysBefore: Number(edit.reminderDaysBefore ?? 1), isActive: edit.isActive ?? true, notes: edit.notes }
      if (edit.id) await api.put(`/api/finance/recurring/${edit.id}`, body); else await api.post('/api/finance/recurring', body)
      toast.success('Recurring expense saved'); setEdit(null); reload()
    } catch (x) { toast.error(x) }
  }
  const mark = async (o: Occurrence, pay: boolean) => {
    try { await api.post(`/api/finance/recurring/occurrences/${o.id}/${pay ? 'pay' : 'skip'}`); toast.success(pay ? `${o.name} marked as paid and recorded as an expense` : 'Skipped'); reload() } catch (x) { toast.error(x) }
  }
  return (
    <div className="row g-3">
      <div className="col-lg-5"><Card className="h-100"><Card.Header>Bills due (next 60 days)</Card.Header><Card.Body>
        {occ.loading ? <Loading /> : occ.data?.length === 0 ? <div className="small text-muted">No pending bills. 🎉</div> : occ.data?.map((o) => (
          <div key={o.id} className="list-row"><div className="flex-grow-1 min-w-0"><div className="fw-medium">{o.name}</div><div className="small text-muted">Due {date(o.dueDate)} · {money(o.amount)}</div></div>
            <StatusBadge status={o.status} /><Button size="sm" variant="outline-success" onClick={() => void mark(o, true)} title="Mark paid"><FiCheck /></Button><Button size="sm" variant="outline-secondary" onClick={() => void mark(o, false)} title="Skip"><FiSkipForward /></Button></div>))}
      </Card.Body></Card></div>
      <div className="col-lg-7"><Card className="h-100"><Card.Header className="d-flex justify-content-between align-items-center">Recurring expenses<Button size="sm" onClick={() => setEdit({ frequency: 'Monthly', startDate: today(), reminderDaysBefore: 1, isActive: true })}><FiPlus /> Add</Button></Card.Header>
        <Card.Body>{rec.loading ? <Loading /> : rec.data?.length === 0 ? <EmptyState icon={<FiRepeat />} title="No recurring expenses" text="Add rent, EMIs, subscriptions or utility bills to get reminders." /> : rec.data?.map((r) => (
          <div key={r.id} className="list-row"><FiRepeat className="text-primary" /><div className="flex-grow-1 min-w-0"><div className="fw-medium">{r.name} {!r.isActive && <Badge bg="secondary">Paused</Badge>} {r.overdueCount > 0 && <Badge bg="danger">{r.overdueCount} overdue</Badge>}</div>
            <div className="small text-muted">{money(r.amount)} · {r.frequency}{r.frequency === 'Custom' ? ` (${r.customIntervalDays} days)` : ''} · {r.categoryName} {r.nextDueDate && `· next ${date(r.nextDueDate)}`}</div></div>
            <Button size="sm" variant="link" onClick={() => setEdit(r)}><FiEdit2 /></Button>
            <Button size="sm" variant="link" className="text-danger" onClick={async () => { if (await confirm({ title: `Delete "${r.name}"?`, body: 'Paid history is kept as expenses.' })) { await api.delete(`/api/finance/recurring/${r.id}`); reload() } }}><FiTrash2 /></Button></div>))}
        </Card.Body></Card></div>
      <Modal show={!!edit} onHide={() => setEdit(null)} centered><Form onSubmit={save}>
        <Modal.Header closeButton><Modal.Title className="h5">{edit?.id ? 'Edit' : 'Add'} recurring expense</Modal.Title></Modal.Header>
        <Modal.Body className="row g-3">
          <Form.Group className="col-12"><Form.Label>Name</Form.Label><Form.Control required value={edit?.name ?? ''} onChange={(e) => setEdit({ ...edit, name: e.target.value })} placeholder="e.g. House rent" /></Form.Group>
          <Form.Group className="col-6"><Form.Label>Amount (₹)</Form.Label><Form.Control required type="number" min="0.01" step="0.01" value={edit?.amount ?? ''} onChange={(e) => setEdit({ ...edit, amount: e.target.value as unknown as number })} /></Form.Group>
          <Form.Group className="col-6"><Form.Label>Category</Form.Label><Form.Select value={edit?.categoryId ?? cats.data?.[0]?.id} onChange={(e) => setEdit({ ...edit, categoryId: e.target.value })}>{cats.data?.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</Form.Select></Form.Group>
          <Form.Group className="col-6"><Form.Label>Frequency</Form.Label><Form.Select value={edit?.frequency ?? 'Monthly'} onChange={(e) => setEdit({ ...edit, frequency: e.target.value })}>{FREQ.map((f) => <option key={f}>{f}</option>)}</Form.Select></Form.Group>
          {edit?.frequency === 'Custom' && <Form.Group className="col-6"><Form.Label>Every N days</Form.Label><Form.Control type="number" min={1} value={edit?.customIntervalDays ?? ''} onChange={(e) => setEdit({ ...edit, customIntervalDays: Number(e.target.value) })} /></Form.Group>}
          <Form.Group className="col-6"><Form.Label>Payment method</Form.Label><Form.Select value={edit?.paymentMethod ?? 'Upi'} onChange={(e) => setEdit({ ...edit, paymentMethod: e.target.value })}>{PAYMENT_METHODS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</Form.Select></Form.Group>
          <Form.Group className="col-6"><Form.Label>Start date</Form.Label><Form.Control type="date" value={edit?.startDate ?? today()} onChange={(e) => setEdit({ ...edit, startDate: e.target.value })} /></Form.Group>
          <Form.Group className="col-6"><Form.Label>End date (optional)</Form.Label><Form.Control type="date" value={edit?.endDate ?? ''} onChange={(e) => setEdit({ ...edit, endDate: e.target.value })} /></Form.Group>
          <Form.Group className="col-6"><Form.Label>Remind days before</Form.Label><Form.Control type="number" min={0} max={60} value={edit?.reminderDaysBefore ?? 1} onChange={(e) => setEdit({ ...edit, reminderDaysBefore: Number(e.target.value) })} /></Form.Group>
          <Form.Check className="col-12" type="switch" id="active" label="Active" checked={edit?.isActive ?? true} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} />
        </Modal.Body>
        <Modal.Footer><Button variant="outline-secondary" onClick={() => setEdit(null)}>Cancel</Button><Button type="submit">Save</Button></Modal.Footer>
      </Form></Modal>
    </div>
  )
}

// ------------------------------------------------------------------ reports
interface Report { period: string; from: string; to: string; totalIncome: number; totalExpense: number; netSavings: number; series: { label: string; income: number; expense: number }[]; categories: { category: string; color?: string; amount: number; percent: number }[]; budgetVsActual: BudgetLine[]; paymentMethods: { category: string; amount: number; percent: number }[] }

function ReportsView() {
  const [period, setPeriod] = useState('monthly')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const r = useAsync(() => api.get<Report>('/api/finance/reports', { params: { period, from: from || undefined, to: to || undefined } }).then((x) => x.data), [period, from, to])
  const qs = `period=${period}${from ? `&from=${from}` : ''}${to ? `&to=${to}` : ''}`
  const d = r.data
  const budgetData = useMemo(() => d?.budgetVsActual.filter((b) => b.budget > 0).map((b) => ({ name: b.categoryName, Budget: b.budget, Actual: b.spent })) ?? [], [d])
  return (
    <>
      <Card className="mb-3"><Card.Body className="d-flex flex-wrap gap-2 align-items-center">
        <Nav variant="pills" activeKey={period} onSelect={(k) => setPeriod(k ?? 'monthly')}>{['daily', 'weekly', 'monthly', 'yearly'].map((p) => <Nav.Item key={p}><Nav.Link eventKey={p} className="text-capitalize">{p}</Nav.Link></Nav.Item>)}</Nav>
        <Form.Control type="date" value={from} onChange={(e) => setFrom(e.target.value)} style={{ maxWidth: 170 }} aria-label="From" />
        <Form.Control type="date" value={to} onChange={(e) => setTo(e.target.value)} style={{ maxWidth: 170 }} aria-label="To" />
        <div className="ms-auto"><ExportMenu url={(fmt) => `/api/finance/reports/export?format=${fmt}&${qs}`} formats={['pdf', 'xlsx']} label="Export report" /></div>
      </Card.Body></Card>
      {!d ? <Loading /> : (
        <>
          <div className="row g-3 mb-3">
            <div className="col-md-4"><StatCard label="Total income" value={money(d.totalIncome)} icon={<FiCreditCard />} tone="success" hint={`${date(d.from)} – ${date(d.to)}`} /></div>
            <div className="col-md-4"><StatCard label="Total expense" value={money(d.totalExpense)} icon={<FiCreditCard />} tone="danger" /></div>
            <div className="col-md-4"><StatCard label="Net savings" value={money(d.netSavings)} icon={<FiCreditCard />} tone={d.netSavings >= 0 ? 'success' : 'danger'} /></div>
          </div>
          <div className="row g-3">
            <div className="col-12"><Card><Card.Header>Income vs expense</Card.Header><Card.Body style={{ height: 300 }}><ResponsiveContainer><BarChart data={d.series}><CartesianGrid strokeDasharray="3 3" stroke="var(--nm-border)" /><XAxis dataKey="label" stroke="var(--nm-muted)" /><YAxis stroke="var(--nm-muted)" /><Tooltip formatter={(v) => money(Number(v))} /><Legend /><Bar dataKey="income" name="Income" fill="#3AAA35" radius={[5, 5, 0, 0]} /><Bar dataKey="expense" name="Expense" fill="#F47B20" radius={[5, 5, 0, 0]} /></BarChart></ResponsiveContainer></Card.Body></Card></div>
            <div className="col-lg-6"><Card className="h-100"><Card.Header>Category-wise expense</Card.Header><Card.Body style={{ height: 320 }}>
              {d.categories.length === 0 ? <div className="small text-muted">No expenses in this period.</div> : <ResponsiveContainer><PieChart><Pie data={d.categories} dataKey="amount" nameKey="category" outerRadius={110} label={(e: { percent?: number }) => `${Math.round((e.percent ?? 0) * 100)}%`}>{d.categories.map((c, i) => <Cell key={c.category} fill={c.color ?? PALETTE[i % PALETTE.length]} />)}</Pie><Tooltip formatter={(v) => money(Number(v))} /><Legend /></PieChart></ResponsiveContainer>}
            </Card.Body></Card></div>
            <div className="col-lg-6"><Card className="h-100"><Card.Header>Budget vs actual ({new Date(d.to).toLocaleString('en-IN', { month: 'long' })})</Card.Header><Card.Body style={{ height: 320 }}>
              {budgetData.length === 0 ? <div className="small text-muted">Set category budgets to compare.</div> : <ResponsiveContainer><BarChart data={budgetData} layout="vertical"><CartesianGrid strokeDasharray="3 3" stroke="var(--nm-border)" /><XAxis type="number" stroke="var(--nm-muted)" /><YAxis type="category" dataKey="name" width={90} stroke="var(--nm-muted)" /><Tooltip formatter={(v) => money(Number(v))} /><Legend /><Bar dataKey="Budget" fill="#0B3D91" /><Bar dataKey="Actual" fill="#F47B20" /></BarChart></ResponsiveContainer>}
            </Card.Body></Card></div>
          </div>
        </>)}
    </>
  )
}

// ------------------------------------------------------------------ categories
function CategoriesView() {
  const toast = useToast()
  const confirm = useConfirm()
  const nav = useNavigate()
  const cats = useCategories()
  const [name, setName] = useState('')
  const [color, setColor] = useState('#0B3D91')
  const add = async (e: React.FormEvent) => {
    e.preventDefault()
    try { await api.post('/api/finance/categories', { name, color, type: 'Expense' }); setName(''); void cats.reload(); toast.success('Category added') } catch (x) { toast.error(x) }
  }
  return (
    <Card><Card.Body>
      <Form onSubmit={add} className="d-flex gap-2 mb-3" style={{ maxWidth: 520 }}>
        <Form.Control required placeholder="New category name" value={name} onChange={(e) => setName(e.target.value)} />
        <Form.Control type="color" value={color} onChange={(e) => setColor(e.target.value)} style={{ maxWidth: 56 }} title="Colour" />
        <Button type="submit"><FiPlus /> Add</Button>
      </Form>
      <div className="row g-2">{cats.data?.map((c) => (
        <div key={c.id} className="col-sm-6 col-lg-4"><div className="d-flex align-items-center gap-2 border rounded-3 p-2">
          <span className="rounded-circle" style={{ width: 14, height: 14, background: c.color ?? '#64748B' }} />
          <span className="flex-grow-1 clickable" onClick={() => nav(`/finance/expenses`)}>{c.name}</span>
          {c.isDefault ? <Badge bg="" className="badge-soft">Default</Badge> : null}
          <Button size="sm" variant="link" className="text-danger p-0" onClick={async () => { if (await confirm({ title: `Delete category "${c.name}"?` })) { try { await api.delete(`/api/finance/categories/${c.id}`); void cats.reload() } catch (x) { toast.error(x) } } }}><FiTrash2 /></Button>
        </div></div>))}</div>
    </Card.Body></Card>
  )
}
