import { useState } from 'react'
import { Button, Card, Form } from 'react-bootstrap'
import { FiBell, FiTrash2 } from 'react-icons/fi'
import { useNavigate } from 'react-router-dom'
import { api } from '../../lib/api'
import { useAsync } from '../../lib/hooks'
import { dateTime } from '../../lib/format'
import { EmptyState, Loading, PageHeader, Pager } from '../../components/ui'

interface N { id: string; title: string; body?: string; link?: string; isRead: boolean; createdAt: string; eventType: string }

export default function NotificationsPage() {
  const nav = useNavigate()
  const [page, setPage] = useState(1)
  const [unread, setUnread] = useState(false)
  const { data, loading, reload } = useAsync(() => api.get('/api/notifications', { params: { page, pageSize: 20, unreadOnly: unread } }).then((r) => r.data as { items: N[]; totalPages: number }), [page, unread])
  return (
    <>
      <PageHeader title="Notifications" subtitle="Alerts, reminders and account activity">
        <Form.Check type="switch" id="unread" label="Unread only" checked={unread} onChange={(e) => { setUnread(e.target.checked); setPage(1) }} className="align-self-center" />
        <Button size="sm" variant="outline-primary" onClick={() => void api.post('/api/notifications/read-all').then(reload)}>Mark all read</Button>
      </PageHeader>
      <Card><Card.Body>
        {loading && !data ? <Loading /> : data?.items.length === 0 ? <EmptyState icon={<FiBell />} title="No notifications" /> : data?.items.map((n) => (
          <div key={n.id} className={`list-row ${n.isRead ? '' : 'fw-semibold'}`}>
            <span className="rounded-circle flex-none" style={{ width: 8, height: 8, background: n.isRead ? 'transparent' : 'var(--nm-orange)' }} />
            <div className="flex-grow-1 min-w-0 clickable" onClick={() => { void api.post(`/api/notifications/${n.id}/read`); if (n.link) nav(n.link); else void reload() }}>
              <div>{n.title}</div>{n.body && <div className="small text-muted fw-normal">{n.body}</div>}<div className="small text-muted fw-normal">{dateTime(n.createdAt)}</div>
            </div>
            <Button size="sm" variant="link" className="text-muted" onClick={() => void api.delete(`/api/notifications/${n.id}`).then(reload)} aria-label="Delete"><FiTrash2 /></Button>
          </div>
        ))}
      </Card.Body><Card.Footer><Pager page={page} totalPages={data?.totalPages ?? 1} onChange={setPage} /></Card.Footer></Card>
    </>
  )
}
