import { useEffect, useMemo, useState } from 'react'
import { Badge, Button, Card, Form, Modal } from 'react-bootstrap'
import { useNavigate } from 'react-router-dom'
import { Bar, BarChart, CartesianGrid, Cell, Legend, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { FiAlertCircle, FiArrowDownLeft, FiArrowUpRight, FiCalendar, FiClock, FiFileText, FiMessageCircle, FiPieChart, FiSettings, FiTarget, FiTrendingDown, FiTrendingUp } from 'react-icons/fi'
import { api } from '../../lib/api'
import { useAsync } from '../../lib/hooks'
import { date, dateTime, money, relative } from '../../lib/format'
import { useAuth } from '../../contexts/AuthContext'
import { ErrorBox, Loading, StatCard } from '../../components/ui'

interface Agenda { kind: string; id: string; title: string; at: string; detail?: string; link?: string }
interface Dash {
  totalIncome: number; totalExpenses: number; monthlyBudget: number; remainingBudget: number; budgetUtilization: number; moneyToReceive: number; moneyToPay: number
  overdueBills: number; unreadNotifications: number; upcomingReminders: Agenda[]; upcomingMeetings: Agenda[]
  recentTransactions: { id: string; kind: string; title: string; category?: string; amount: number; date: string }[]
  recentDocuments: { id: string; name: string; category: string; extension: string; updatedAt: string }[]
  recentDiary: { id: string; title: string; date: string; mood?: string }[]
  recentMessages: { conversationId: string; friendName: string; lastMessage?: string; at?: string; unread: number }[]
  trend: { label: string; income: number; expense: number }[]; categoryBreakdown: { name: string; color?: string; amount: number }[]; layoutJson?: string
}

const WIDGETS = [['stats', 'Summary cards'], ['charts', 'Income vs expense & categories'], ['agenda', 'Upcoming reminders & meetings'], ['transactions', 'Recent transactions'], ['documents', 'Recent documents'], ['diary', 'Recent diary entries'], ['messages', 'Recent messages']] as const
type WidgetKey = (typeof WIDGETS)[number][0]
const PALETTE = ['#0B3D91', '#F47B20', '#3AAA35', '#7B3FBF', '#E6397E', '#F9A825', '#1E88E5', '#00A0A0']

export default function DashboardPage() {
  const { me } = useAuth()
  const nav = useNavigate()
  const { data, loading, error, reload } = useAsync(() => api.get<Dash>('/api/dashboard').then((r) => r.data), [])
  const [hidden, setHidden] = useState<WidgetKey[]>([])
  const [customize, setCustomize] = useState(false)
  useEffect(() => { if (data?.layoutJson) try { setHidden(JSON.parse(data.layoutJson).hidden ?? []) } catch { /* ignore */ } }, [data?.layoutJson])
  const saveLayout = (h: WidgetKey[]) => { setHidden(h); void api.put('/api/me/dashboard-layout', { layoutJson: JSON.stringify({ hidden: h }) }) }
  const show = (k: WidgetKey) => !hidden.includes(k)
  const greeting = useMemo(() => { const h = new Date().getHours(); return h < 12 ? 'Good morning' : h < 17 ? 'Good afternoon' : 'Good evening' }, [])

  if (loading && !data) return <Loading />
  if (error) return <ErrorBox error={error} onRetry={reload} />
  if (!data) return null
  const agendaIcon = (k: string) => k.startsWith('bill') ? <FiFileText /> : k.startsWith('udhar') ? <FiArrowUpRight /> : k === 'meeting' ? <FiCalendar /> : <FiClock />

  return (
    <>
      <div className="page-header">
        <div><h1>{greeting}, {me?.fullName.split(' ')[0]} 👋</h1><div className="text-muted small">नमस्ते! Here's your life at a glance for {new Date().toLocaleDateString('en-IN', { month: 'long', year: 'numeric' })}.</div></div>
        <Button variant="outline-secondary" size="sm" onClick={() => setCustomize(true)}><FiSettings /> Customize</Button>
      </div>
      {data.overdueBills > 0 && <div className="alert alert-warning d-flex align-items-center gap-2 clickable" onClick={() => nav('/finance/recurring')}><FiAlertCircle /> You have {data.overdueBills} overdue bill(s). Tap to review.</div>}

      {show('stats') && (
        <div className="row g-3 mb-3">
          <div className="col-6 col-xl-3"><StatCard label="Total income (month)" value={money(data.totalIncome)} icon={<FiTrendingUp />} tone="success" onClick={() => nav('/finance/income')} /></div>
          <div className="col-6 col-xl-3"><StatCard label="Total expenses (month)" value={money(data.totalExpenses)} icon={<FiTrendingDown />} tone="danger" onClick={() => nav('/finance/expenses')} /></div>
          <div className="col-6 col-xl-3"><StatCard label="Monthly budget" value={data.monthlyBudget ? money(data.monthlyBudget) : 'Not set'} icon={<FiTarget />} tone="primary" hint={data.monthlyBudget ? `${data.budgetUtilization}% used` : undefined} onClick={() => nav('/finance/budget')} /></div>
          <div className="col-6 col-xl-3"><StatCard label="Remaining budget" value={data.monthlyBudget ? money(data.remainingBudget) : '—'} icon={<FiPieChart />} tone={data.remainingBudget < 0 ? 'danger' : 'info'} onClick={() => nav('/finance/budget')} /></div>
          <div className="col-6 col-xl-3"><StatCard label="Money to receive" value={money(data.moneyToReceive)} icon={<FiArrowDownLeft />} tone="success" onClick={() => nav('/udhar')} /></div>
          <div className="col-6 col-xl-3"><StatCard label="Money to pay" value={money(data.moneyToPay)} icon={<FiArrowUpRight />} tone="warning" onClick={() => nav('/udhar')} /></div>
          <div className="col-6 col-xl-3"><StatCard label="Upcoming reminders" value={data.upcomingReminders.length} icon={<FiClock />} tone="info" onClick={() => nav('/reminders')} /></div>
          <div className="col-6 col-xl-3"><StatCard label="Upcoming meetings" value={data.upcomingMeetings.length} icon={<FiCalendar />} tone="primary" onClick={() => nav('/calendar')} /></div>
        </div>
      )}

      {show('charts') && (
        <div className="row g-3 mb-3">
          <div className="col-lg-8"><Card className="h-100"><Card.Header>Income vs expenses — last 6 months</Card.Header><Card.Body style={{ height: 290 }}>
            <ResponsiveContainer><BarChart data={data.trend}><CartesianGrid strokeDasharray="3 3" stroke="var(--nm-border)" /><XAxis dataKey="label" stroke="var(--nm-muted)" /><YAxis stroke="var(--nm-muted)" tickFormatter={(v) => `₹${Number(v) >= 1000 ? `${Math.round(Number(v) / 1000)}k` : v}`} />
              <Tooltip formatter={(v) => money(Number(v))} contentStyle={{ background: 'var(--nm-surface)', border: '1px solid var(--nm-border)' }} /><Legend />
              <Bar dataKey="income" name="Income" fill="#3AAA35" radius={[6, 6, 0, 0]} /><Bar dataKey="expense" name="Expense" fill="#F47B20" radius={[6, 6, 0, 0]} /></BarChart></ResponsiveContainer>
          </Card.Body></Card></div>
          <div className="col-lg-4"><Card className="h-100"><Card.Header>This month by category</Card.Header><Card.Body style={{ height: 290 }}>
            {data.categoryBreakdown.length === 0 ? <div className="empty-state">No expenses this month yet.</div> :
              <ResponsiveContainer><PieChart><Pie data={data.categoryBreakdown} dataKey="amount" nameKey="name" innerRadius={55} outerRadius={95} paddingAngle={2}>
                {data.categoryBreakdown.map((c, i) => <Cell key={c.name} fill={c.color ?? PALETTE[i % PALETTE.length]} />)}</Pie><Tooltip formatter={(v) => money(Number(v))} /><Legend /></PieChart></ResponsiveContainer>}
          </Card.Body></Card></div>
        </div>
      )}

      <div className="row g-3">
        {show('agenda') && <div className="col-lg-6"><Card className="h-100"><Card.Header>Upcoming (next 14 days)</Card.Header><Card.Body>
          {[...data.upcomingMeetings, ...data.upcomingReminders].sort((a, b) => a.at.localeCompare(b.at)).slice(0, 8).map((a) => (
            <div key={a.kind + a.id + a.at} className="list-row clickable" onClick={() => a.link && nav(a.link)}>
              <span className={`stat-card p-0`}><span className={`icon ${a.kind.includes('overdue') ? 'bg-soft-danger' : 'badge-soft'}`} style={{ width: 36, height: 36, fontSize: '1rem' }}>{agendaIcon(a.kind)}</span></span>
              <div className="flex-grow-1 min-w-0"><div className="fw-medium truncate">{a.title}</div><div className="small text-muted">{dateTime(a.at)} {a.detail && `· ${a.detail}`}</div></div>
              {a.kind.includes('overdue') && <Badge bg="danger">Overdue</Badge>}
            </div>))}
          {data.upcomingMeetings.length + data.upcomingReminders.length === 0 && <div className="small text-muted">Nothing scheduled. Enjoy your free time!</div>}
        </Card.Body></Card></div>}
        {show('transactions') && <div className="col-lg-6"><Card className="h-100"><Card.Header>Recent transactions</Card.Header><Card.Body>
          {data.recentTransactions.length === 0 ? <div className="small text-muted">No transactions yet. Use Quick Add to record one.</div> : data.recentTransactions.map((t) => (
            <div key={t.id} className="list-row clickable" onClick={() => nav(t.kind === 'income' ? '/finance/income' : '/finance/expenses')}>
              <div className="flex-grow-1 min-w-0"><div className="fw-medium truncate">{t.title}</div><div className="small text-muted">{t.category} · {date(t.date)}</div></div>
              <span className={`fw-semibold ${t.kind === 'income' ? 'money-in' : 'money-out'}`}>{t.kind === 'income' ? '+' : '−'}{money(t.amount)}</span>
            </div>))}
        </Card.Body></Card></div>}
        {show('documents') && <div className="col-md-6 col-xl-4"><Card className="h-100"><Card.Header>Recent documents</Card.Header><Card.Body>
          {data.recentDocuments.length === 0 ? <div className="small text-muted">No documents uploaded.</div> : data.recentDocuments.map((d) => (
            <div key={d.id} className="list-row clickable" onClick={() => nav(`/documents?open=${d.id}`)}><FiFileText className="text-primary" /><div className="flex-grow-1 min-w-0"><div className="truncate">{d.name}</div><div className="small text-muted">{d.category} · {relative(d.updatedAt)}</div></div></div>))}
        </Card.Body></Card></div>}
        {show('diary') && <div className="col-md-6 col-xl-4"><Card className="h-100"><Card.Header>Recent diary entries</Card.Header><Card.Body>
          {data.recentDiary.length === 0 ? <div className="small text-muted">Start writing your first entry.</div> : data.recentDiary.map((d) => (
            <div key={d.id} className="list-row clickable" onClick={() => nav(`/diary/${d.id}`)}><div className="flex-grow-1 min-w-0"><div className="truncate">{d.title}</div><div className="small text-muted">{date(d.date)} {d.mood && `· ${d.mood}`}</div></div></div>))}
        </Card.Body></Card></div>}
        {show('messages') && <div className="col-md-6 col-xl-4"><Card className="h-100"><Card.Header>Recent messages</Card.Header><Card.Body>
          {data.recentMessages.length === 0 ? <div className="small text-muted">No conversations yet.</div> : data.recentMessages.map((m) => (
            <div key={m.conversationId} className="list-row clickable" onClick={() => nav(`/chat/${m.conversationId}`)}><FiMessageCircle className="text-primary" />
              <div className="flex-grow-1 min-w-0"><div className="fw-medium">{m.friendName}</div><div className="small text-muted truncate">{m.lastMessage}</div></div>
              {m.unread > 0 && <Badge pill bg="primary">{m.unread}</Badge>}</div>))}
        </Card.Body></Card></div>}
      </div>

      <Modal show={customize} onHide={() => setCustomize(false)} centered>
        <Modal.Header closeButton><Modal.Title className="h5">Customize dashboard</Modal.Title></Modal.Header>
        <Modal.Body>{WIDGETS.map(([k, l]) => <Form.Check key={k} type="switch" id={`w-${k}`} label={l} className="mb-2" checked={show(k)} onChange={(e) => saveLayout(e.target.checked ? hidden.filter((h) => h !== k) : [...hidden, k])} />)}</Modal.Body>
      </Modal>
    </>
  )
}
