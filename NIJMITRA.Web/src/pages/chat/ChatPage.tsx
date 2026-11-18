import { useCallback, useEffect, useRef, useState } from 'react'
import { Alert, Badge, Button, Card, Dropdown, Form, InputGroup, Modal, Nav, Spinner } from 'react-bootstrap'
import { useNavigate, useParams } from 'react-router-dom'
import { FiAlertCircle, FiArrowDown, FiArrowLeft, FiCheck, FiClock, FiCopy, FiCornerUpLeft, FiDownload, FiEyeOff, FiFile, FiMessageCircle, FiMoreVertical, FiPaperclip, FiSearch, FiSend, FiShield, FiTrash2, FiUserPlus, FiX } from 'react-icons/fi'
import { BiCheckDouble } from 'react-icons/bi'
import { api, blobUrl, downloadFile, uploadFile } from '../../lib/api'
import { useAsync } from '../../lib/hooks'
import { compressImage, imageCache } from '../../lib/image'
import { bytes, dateTime, relative, time } from '../../lib/format'
import { useAuth } from '../../contexts/AuthContext'
import { useRealtime, useRealtimeEvent } from '../../contexts/RealtimeContext'
import { useToast } from '../../contexts/ToastContext'
import { Avatar, EmptyState, Loading, PageHeader, PreviewModal, useConfirm } from '../../components/ui'

interface Friend { userId: string; publicId: string; fullName: string; hasAvatar: boolean; isOnline: boolean; lastSeenAt?: string; conversationId: string; lastMessage?: string; lastMessageAt?: string; unreadCount: number; isBlocked: boolean; blockedMe: boolean; disappearingSeconds?: number | null }
interface Att { fileId: string; fileName: string; contentType: string; sizeBytes: number }
interface Msg { id: string; conversationId: string; senderId: string; type: string; body?: string; replyToMessageId?: string; replyPreview?: string; createdAt: string; isDeleted: boolean; expiresAt?: string; status: string; attachments: Att[]; local?: LocalState }
interface LocalFile { name: string; type: string; size: number; url: string; progress: number }
interface LocalState { raw: File[]; files: LocalFile[]; failed?: boolean }
interface Req { id: string; user: { id: string; publicId: string; fullName: string }; message?: string; createdAt: string }

export default function ChatPage() {
  const { conversationId } = useParams()
  const nav = useNavigate()
  const { me } = useAuth()
  const [tab, setTab] = useState<'chats' | 'requests' | 'add'>('chats')
  const friends = useAsync(() => api.get<Friend[]>('/api/chat/friends').then((r) => r.data), [])
  const incoming = useAsync(() => api.get<Req[]>('/api/chat/requests', { params: { incoming: true } }).then((r) => r.data), [])
  const reloadAll = () => { void friends.reload(); void incoming.reload() }
  useRealtimeEvent<Msg>('message', (m) => friends.setData((fs) => fs?.map((f) => f.conversationId === m.conversationId ? { ...f, lastMessage: m.body ?? '📎 Attachment', lastMessageAt: m.createdAt, unreadCount: m.senderId !== me?.id && m.conversationId !== conversationId ? f.unreadCount + 1 : f.unreadCount } : f).sort((a, b) => (b.lastMessageAt ?? '').localeCompare(a.lastMessageAt ?? ''))))
  useRealtimeEvent<{ userId: string; isOnline: boolean; lastSeenAt: string }>('presence', (p) => friends.setData((fs) => fs?.map((f) => f.userId === p.userId ? { ...f, isOnline: p.isOnline, lastSeenAt: p.lastSeenAt } : f)))
  useRealtimeEvent('friendRequest', () => void incoming.reload())
  useRealtimeEvent('friendAccepted', () => void friends.reload())
  const active = friends.data?.find((f) => f.conversationId === conversationId)
  return (
    <div className={`chat-page ${conversationId ? 'has-active' : ''}`}>
      <PageHeader title="Friends & Chat" subtitle="Private conversations with people you trust" icon={<FiMessageCircle />} />
      <Card className="overflow-hidden chat-card">
        <div className={`chat-shell ${conversationId ? 'has-active' : ''}`}>
          <div className="chat-list">
            <div className="p-2 border-bottom"><Nav variant="pills" activeKey={tab} onSelect={(k) => setTab(k as typeof tab)} className="flex-nowrap small">
              <Nav.Item><Nav.Link eventKey="chats">Chats</Nav.Link></Nav.Item>
              <Nav.Item><Nav.Link eventKey="requests">Requests {incoming.data && incoming.data.length > 0 && <Badge pill bg="danger">{incoming.data.length}</Badge>}</Nav.Link></Nav.Item>
              <Nav.Item><Nav.Link eventKey="add"><FiUserPlus /> Add</Nav.Link></Nav.Item></Nav></div>
            {tab === 'chats' && (friends.loading ? <Loading /> : friends.data?.length === 0 ? <EmptyState icon={<FiUserPlus />} title="No friends yet" text="Share your ID or an invite code to connect." action={<Button size="sm" onClick={() => setTab('add')}>Add friend</Button>} /> :
              friends.data?.map((f) => (
                <div key={f.userId} className={`chat-item ${f.conversationId === conversationId ? 'active' : ''}`} onClick={() => { nav(`/chat/${f.conversationId}`); friends.setData((fs) => fs?.map((x) => x.userId === f.userId ? { ...x, unreadCount: 0 } : x)) }}>
                  <span className="position-relative"><Avatar name={f.fullName} src={f.hasAvatar ? `/api/me/avatar/${f.userId}` : null} size={42} /><span className="presence-dot" style={{ background: f.isOnline ? '#3AAA35' : '#94A3B8' }} /></span>
                  <div className="flex-grow-1 min-w-0"><div className="d-flex justify-content-between"><span className="fw-semibold truncate">{f.fullName}</span><small className="text-muted">{f.lastMessageAt ? relative(f.lastMessageAt) : ''}</small></div>
                    <div className="d-flex justify-content-between"><small className="text-muted truncate">{f.isBlocked ? 'Blocked' : f.lastMessage ?? (f.isOnline ? 'Online' : `Last seen ${relative(f.lastSeenAt)}`)}</small>{f.unreadCount > 0 && <Badge pill bg="primary">{f.unreadCount}</Badge>}</div></div>
                </div>)))}
            {tab === 'requests' && <Requests incoming={incoming.data ?? []} onChange={reloadAll} />}
            {tab === 'add' && <AddFriend onAdded={() => { reloadAll(); setTab('chats') }} />}
          </div>
          <div className="chat-pane">
            {active ? <Conversation friend={active} onBack={() => nav('/chat')} onChanged={() => void friends.reload()} />
              : <div className="d-flex flex-column align-items-center justify-content-center h-100 text-muted p-4 text-center"><FiShield size={44} className="mb-2 text-primary" /><h6>Select a conversation</h6><p className="small" style={{ maxWidth: 360 }}>Messages are protected in transit (HTTPS) and visible only to the people in the conversation.</p></div>}
          </div>
        </div>
      </Card>
    </div>
  )
}

function Requests({ incoming, onChange }: { incoming: Req[]; onChange: () => void }) {
  const toast = useToast()
  const outgoing = useAsync(() => api.get<Req[]>('/api/chat/requests', { params: { incoming: false } }).then((r) => r.data), [])
  const act = async (url: string, msg: string) => { try { await api.post(url); toast.success(msg); onChange(); void outgoing.reload() } catch (e) { toast.error(e) } }
  return (
    <div className="p-3 small">
      <div className="fw-semibold mb-2">Received</div>
      {incoming.length === 0 && <div className="text-muted mb-3">No pending requests.</div>}
      {incoming.map((r) => <div key={r.id} className="list-row"><Avatar name={r.user.fullName} size={34} /><div className="flex-grow-1"><div className="fw-medium">{r.user.fullName}</div><div className="text-muted">{r.user.publicId}{r.message && ` · “${r.message}”`}</div></div>
        <Button size="sm" onClick={() => void act(`/api/chat/requests/${r.id}/accept`, 'Friend added')}><FiCheck /></Button><Button size="sm" variant="outline-secondary" onClick={() => void act(`/api/chat/requests/${r.id}/reject`, 'Request declined')}><FiX /></Button></div>)}
      <div className="fw-semibold mb-2 mt-3">Sent</div>
      {outgoing.data?.length === 0 && <div className="text-muted">None.</div>}
      {outgoing.data?.map((r) => <div key={r.id} className="list-row"><div className="flex-grow-1">{r.user.fullName} <span className="text-muted">{r.user.publicId}</span></div><Button size="sm" variant="link" onClick={() => void api.delete(`/api/chat/requests/${r.id}`).then(() => outgoing.reload())}>Cancel</Button></div>)}
    </div>
  )
}

function AddFriend({ onAdded }: { onAdded: () => void }) {
  const toast = useToast()
  const { me } = useAuth()
  const [id, setId] = useState('')
  const [found, setFound] = useState<{ id: string; publicId: string; fullName: string; relationship: string } | null>(null)
  const [code, setCode] = useState('')
  const invites = useAsync(() => api.get('/api/chat/invitations').then((r) => r.data as { id: string; code: string; expiresAt: string; maxUses: number; usedCount: number; isActive: boolean }[]), [])
  const find = async () => { setFound(null); try { setFound((await api.get(`/api/chat/users/${encodeURIComponent(id.trim())}`)).data) } catch { toast.push('error', 'No user with this NijMitra ID.') } }
  return (
    <div className="p-3 small">
      <div className="fw-semibold mb-1">Your NijMitra ID</div>
      <div className="d-flex align-items-center gap-2 mb-3"><code className="fs-6">{me?.publicId}</code><Button size="sm" variant="link" onClick={() => { void navigator.clipboard.writeText(me?.publicId ?? ''); toast.success('Copied') }}><FiCopy /></Button></div>
      <div className="fw-semibold mb-1">Find by NijMitra ID</div>
      <InputGroup size="sm" className="mb-2"><Form.Control placeholder="e.g. NM100025" value={id} onChange={(e) => setId(e.target.value.toUpperCase())} /><Button onClick={() => void find()} disabled={!id}><FiSearch /></Button></InputGroup>
      {found && <div className="list-row"><Avatar name={found.fullName} size={34} /><div className="flex-grow-1"><div className="fw-medium">{found.fullName}</div><div className="text-muted">{found.publicId}</div></div>
        {found.relationship === 'none' || found.relationship === 'request_received'
          ? <Button size="sm" onClick={async () => { try { await api.post('/api/chat/requests', { publicId: found.publicId }); toast.success(found.relationship === 'request_received' ? 'You are now friends' : 'Request sent'); setFound(null); onAdded() } catch (e) { toast.error(e) } }}>{found.relationship === 'request_received' ? 'Accept' : 'Send request'}</Button>
          : <Badge bg="" className="badge-soft">{found.relationship.replace('_', ' ')}</Badge>}</div>}
      <hr />
      <div className="fw-semibold mb-1">Have an invite code?</div>
      <InputGroup size="sm" className="mb-3"><Form.Control placeholder="NM-XXXXXXXX" value={code} onChange={(e) => setCode(e.target.value.toUpperCase())} />
        <Button disabled={!code} onClick={async () => { try { await api.post('/api/chat/invitations/accept', null, { params: { code } }); toast.success('Connected!'); setCode(''); onAdded() } catch (e) { toast.error(e) } }}>Join</Button></InputGroup>
      <div className="fw-semibold mb-1">Invite someone</div>
      <Button size="sm" variant="outline-primary" onClick={async () => { try { await api.post('/api/chat/invitations', { expiresInHours: 168, maxUses: 1 }); void invites.reload() } catch (e) { toast.error(e) } }}>Generate invite code</Button>
      {invites.data?.filter((i) => i.isActive).map((i) => <div key={i.id} className="list-row"><code className="flex-grow-1">{i.code}</code><span className="text-muted">expires {relative(i.expiresAt).replace(' ago', '')}</span>
        <Button size="sm" variant="link" onClick={() => { void navigator.clipboard.writeText(`Join me on NijMitra: open nijmitra.com → Friends & Chat → enter invite code ${i.code}`); toast.success('Invitation copied') }}><FiCopy /></Button>
        <Button size="sm" variant="link" className="text-danger" onClick={() => void api.delete(`/api/chat/invitations/${i.id}`).then(() => invites.reload())}><FiX /></Button></div>)}
    </div>
  )
}

const fileUrl = (messageId: string, fileId: string) => `/api/chat/messages/${messageId}/files/${fileId}?inline=true`

function AttachmentView({ m, a, onPreview }: { m: Msg; a: Att; onPreview: () => void }) {
  const key = fileUrl(m.id, a.fileId)
  const isImg = a.contentType.startsWith('image/')
  const [src, setSrc] = useState<string | undefined>(() => (isImg ? imageCache.get(key) : undefined))
  useEffect(() => {
    if (!isImg || src) return
    let alive = true
    blobUrl(key).then((x) => { imageCache.set(key, x); if (alive) setSrc(x) }).catch(() => {})
    return () => { alive = false }
  }, [isImg, key, src])
  if (isImg) return src ? <img src={src} className="attachment clickable" alt={a.fileName} onClick={onPreview} draggable={false} /> : <div className="attachment-ph"><Spinner size="sm" /></div>
  return <div className="d-flex align-items-center gap-2 small clickable" onClick={onPreview}><FiFile /><span className="truncate">{a.fileName}</span><span className="opacity-75">{bytes(a.sizeBytes)}</span>
    <Button size="sm" variant="link" className="p-0 text-reset" onClick={(e) => { e.stopPropagation(); void downloadFile(`/api/chat/messages/${m.id}/files/${a.fileId}`, a.fileName) }}><FiDownload /></Button></div>
}

/** A file that is still uploading: shown immediately from the local copy with a progress bar. */
function LocalAttachment({ f, failed }: { f: LocalFile; failed?: boolean }) {
  const bar = !failed && f.progress < 100 && <div className="upload-progress"><span style={{ width: `${Math.max(4, f.progress)}%` }} /></div>
  if (f.type.startsWith('image/')) return <div className="position-relative d-inline-block"><img src={f.url} className="attachment" alt={f.name} style={{ opacity: failed ? 0.5 : 0.85 }} draggable={false} />{bar}</div>
  return <div className="small"><div className="d-flex align-items-center gap-2"><FiFile /><span className="truncate">{f.name}</span><span className="opacity-75">{bytes(f.size)}</span></div>{bar}</div>
}

let tempSeq = 0

function Conversation({ friend, onBack, onChanged }: { friend: Friend; onBack: () => void; onChanged: () => void }) {
  const { me } = useAuth()
  const toast = useToast()
  const confirm = useConfirm()
  const { invoke } = useRealtime()
  const [msgs, setMsgs] = useState<Msg[]>([])
  const [loading, setLoading] = useState(true)
  const [hasMore, setHasMore] = useState(true)
  const [text, setText] = useState('')
  const [reply, setReply] = useState<Msg | null>(null)
  const [typing, setTyping] = useState(false)
  const [files, setFiles] = useState<File[]>([])
  const [search, setSearch] = useState<string | null>(null)
  const [preview, setPreview] = useState<{ url: string; name: string; type: string } | null>(null)
  const [report, setReport] = useState(false)
  const [showJump, setShowJump] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  const inner = useRef<HTMLDivElement>(null)
  const input = useRef<HTMLTextAreaElement>(null)
  const stick = useRef(true) // true while the user is at (or near) the bottom of the conversation
  const typingTimer = useRef<ReturnType<typeof setTimeout>>(undefined)
  const theirTypingTimer = useRef<ReturnType<typeof setTimeout>>(undefined)
  const lastTypingSent = useRef(0)
  const conv = friend.conversationId

  const scrollBottom = (behavior: ScrollBehavior = 'auto') => requestAnimationFrame(() => { const el = box.current; if (el) el.scrollTo({ top: el.scrollHeight, behavior }) })
  const onScroll = () => {
    const el = box.current
    if (!el) return
    stick.current = el.scrollHeight - el.scrollTop - el.clientHeight < 90
    if (stick.current) setShowJump(false)
  }
  // Keeps the view pinned to the newest message while content grows (new messages, images finishing loading, typing indicator).
  useEffect(() => {
    const el = inner.current
    if (!el) return
    const ro = new ResizeObserver(() => { if (stick.current && box.current) box.current.scrollTop = box.current.scrollHeight })
    ro.observe(el)
    return () => ro.disconnect()
  }, [loading])

  const load = useCallback(async (before?: string) => {
    const r = await api.get<Msg[]>(`/api/chat/conversations/${conv}/messages`, { params: { before, take: 40, search: search || undefined } })
    setHasMore(r.data.length === 40)
    return r.data
  }, [conv, search])
  useEffect(() => {
    let cancelled = false
    setLoading(true); setMsgs([]); stick.current = true; setShowJump(false)
    load().then((d) => { if (!cancelled) { setMsgs(d); setLoading(false); scrollBottom() } }).catch(() => setLoading(false))
    void api.post(`/api/chat/conversations/${conv}/read`).catch(() => {})
    return () => { cancelled = true }
  }, [conv, load])

  useRealtimeEvent<Msg>('message', (m) => {
    if (m.conversationId !== conv) return
    setMsgs((x) => (x.some((y) => y.id === m.id) ? x : [...x, m]))
    if (m.senderId !== me?.id) { setTyping(false); void invoke('MarkRead', conv).catch(() => {}); if (!stick.current) setShowJump(true) }
  })
  useRealtimeEvent<{ id: string; conversationId: string }>('messageDeleted', (d) => d.conversationId === conv && setMsgs((x) => x.map((m) => (m.id === d.id ? { ...m, isDeleted: true, body: undefined, attachments: [] } : m))))
  useRealtimeEvent<{ id: string; conversationId: string }>('messageExpired', (d) => d.conversationId === conv && setMsgs((x) => x.filter((m) => m.id !== d.id)))
  useRealtimeEvent<{ conversationId: string; userId: string; isTyping: boolean }>('typing', (t) => {
    if (t.conversationId !== conv || t.userId !== friend.userId) return
    setTyping(t.isTyping)
    clearTimeout(theirTypingTimer.current)
    if (t.isTyping) theirTypingTimer.current = setTimeout(() => setTyping(false), 6000) // safety: never show "typing" forever
  })
  useRealtimeEvent<{ conversationId: string; at: string }>('read', (r) => r.conversationId === conv && setMsgs((x) => x.map((m) => (m.senderId === me?.id && !m.local && m.createdAt <= r.at ? { ...m, status: 'read' } : m))))
  useRealtimeEvent<{ conversationId: string; at: string }>('delivered', (r) => r.conversationId === conv && setMsgs((x) => x.map((m) => (m.senderId === me?.id && m.status === 'sent' && m.createdAt <= r.at ? { ...m, status: 'delivered' } : m))))
  useRealtimeEvent('conversationUpdated', () => onChanged())

  const stopTyping = () => {
    clearTimeout(typingTimer.current)
    if (lastTypingSent.current) { lastTypingSent.current = 0; void invoke('Typing', conv, false).catch(() => {}) }
  }
  const onType = (v: string) => {
    setText(v)
    const now = Date.now()
    if (now - lastTypingSent.current > 2500) { lastTypingSent.current = now; void invoke('Typing', conv, true).catch(() => {}) } // throttled: not one call per key press
    clearTimeout(typingTimer.current)
    typingTimer.current = setTimeout(stopTyping, 3000)
  }
  useEffect(() => () => { clearTimeout(typingTimer.current); clearTimeout(theirTypingTimer.current) }, [])

  const patchLocal = (id: string, fn: (l: LocalState) => LocalState) => setMsgs((x) => x.map((m) => (m.id === id && m.local ? { ...m, local: fn(m.local) } : m)))
  const deliver = async (temp: Msg) => {
    const { raw, files: lf } = temp.local!
    try {
      // Uploads run in parallel; large photos are resized in the browser first.
      const ids = await Promise.all(raw.map(async (f, i) => {
        const small = await compressImage(f)
        const r = await uploadFile<{ id: string }>('/api/uploads?purpose=chat', small, undefined, (p) => patchLocal(temp.id, (l) => ({ ...l, files: l.files.map((x, j) => (j === i ? { ...x, progress: p } : x)) })))
        return r.id
      }))
      const r = await api.post<Msg>(`/api/chat/conversations/${conv}/messages`, { body: temp.body ?? null, replyToMessageId: temp.replyToMessageId ?? null, fileIds: ids })
      const used = new Set<string>()
      for (const a of r.data.attachments) {
        const i = ids.indexOf(a.fileId)
        if (i >= 0 && a.contentType.startsWith('image/')) { imageCache.set(fileUrl(r.data.id, a.fileId), lf[i].url); used.add(lf[i].url) } // show our own photo instantly, no re-download
      }
      lf.forEach((f) => { if (!used.has(f.url)) URL.revokeObjectURL(f.url) })
      setMsgs((x) => (x.some((m) => m.id === r.data.id) ? x.filter((m) => m.id !== temp.id) : x.map((m) => (m.id === temp.id ? r.data : m))))
    } catch (e) {
      patchLocal(temp.id, (l) => ({ ...l, failed: true }))
      toast.error(e)
    }
  }
  const send = (e?: React.FormEvent) => {
    e?.preventDefault()
    const body = text.trim()
    if (!body && files.length === 0) return
    const raw = files
    const temp: Msg = {
      id: `tmp-${Date.now()}-${++tempSeq}`, conversationId: conv, senderId: me!.id, type: 'Text', body: body || undefined,
      replyToMessageId: reply?.id, replyPreview: reply ? (reply.body ?? '📎 Attachment') : undefined, createdAt: new Date().toISOString(),
      isDeleted: false, status: 'sending', attachments: [],
      local: { raw, files: raw.map((f) => ({ name: f.name, type: f.type, size: f.size, url: URL.createObjectURL(f), progress: 0 })) },
    }
    // The message appears at once; the network work happens in the background.
    setMsgs((x) => [...x, temp]); setText(''); setFiles([]); setReply(null); stopTyping()
    stick.current = true; setShowJump(false); scrollBottom()
    input.current?.focus()
    void deliver(temp)
  }
  const retry = (m: Msg) => { patchLocal(m.id, (l) => ({ ...l, failed: false })); void deliver(m) }
  const discard = (m: Msg) => { m.local?.files.forEach((f) => URL.revokeObjectURL(f.url)); setMsgs((x) => x.filter((y) => y.id !== m.id)) }
  const loadOlder = async () => {
    const first = msgs.find((m) => !m.local)
    if (!first || !box.current) return
    const el = box.current
    const prevHeight = el.scrollHeight
    const older = await load(first.createdAt)
    setMsgs((x) => [...older.filter((o) => !x.some((m) => m.id === o.id)), ...x])
    requestAnimationFrame(() => { el.scrollTop = el.scrollHeight - prevHeight + el.scrollTop }) // keep the reading position
  }
  const addFiles = (list: FileList | File[] | null | undefined) => { const arr = Array.from(list ?? []); if (arr.length) setFiles((f) => [...f, ...arr].slice(0, 10)) }
  const disabled = friend.isBlocked || friend.blockedMe
  const tick = (m: Msg) => m.local ? (m.local.failed ? <FiAlertCircle className="text-warning" /> : <FiClock />) : m.status === 'read' ? <BiCheckDouble className="text-info" /> : m.status === 'delivered' ? <BiCheckDouble /> : <FiCheck />
  const rows = Math.min(5, Math.max(1, text.split('\n').length))
  return (
    <>
      <div className="d-flex align-items-center gap-2 p-2 border-bottom flex-shrink-0">
        <Button variant="link" className="d-md-none text-body p-1" onClick={onBack} aria-label="Back"><FiArrowLeft /></Button>
        <Avatar name={friend.fullName} src={friend.hasAvatar ? `/api/me/avatar/${friend.userId}` : null} size={38} />
        <div className="flex-grow-1 min-w-0"><div className="fw-semibold truncate">{friend.fullName}</div><div className="small text-muted">{typing ? <span className="text-primary">typing…</span> : friend.isOnline ? 'Online' : `Last seen ${relative(friend.lastSeenAt)}`}</div></div>
        {friend.disappearingSeconds && <Badge bg="" className="badge-soft" title="Disappearing messages on"><FiClock /> {friend.disappearingSeconds >= 86400 ? `${friend.disappearingSeconds / 86400}d` : `${friend.disappearingSeconds / 3600}h`}</Badge>}
        <Button variant="link" className="text-body" onClick={() => setSearch(search === null ? '' : null)} aria-label="Search"><FiSearch /></Button>
        <Dropdown align="end"><Dropdown.Toggle variant="link" className="text-body no-caret"><FiMoreVertical /></Dropdown.Toggle><Dropdown.Menu>
          <Dropdown.Header>Disappearing messages</Dropdown.Header>
          {[[0, 'Off'], [3600, '1 hour'], [86400, '24 hours'], [604800, '7 days']].map(([s, l]) => <Dropdown.Item key={s} onClick={() => void api.put(`/api/chat/conversations/${conv}/disappearing`, { seconds: s }).then(onChanged)}>{l}</Dropdown.Item>)}
          <Dropdown.Divider />
          <Dropdown.Item onClick={async () => { await api.post(`/api/chat/users/${friend.userId}/block`, null, { params: { value: !friend.isBlocked } }); onChanged() }}>{friend.isBlocked ? 'Unblock' : 'Block'} user</Dropdown.Item>
          <Dropdown.Item onClick={() => setReport(true)}>Report user</Dropdown.Item>
          <Dropdown.Item className="text-danger" onClick={async () => { if (await confirm({ title: `Remove ${friend.fullName} from friends?`, confirmText: 'Remove' })) { await api.delete(`/api/chat/friends/${friend.userId}`); onChanged(); onBack() } }}>Remove friend</Dropdown.Item>
        </Dropdown.Menu></Dropdown>
      </div>
      {search !== null && <div className="p-2 border-bottom flex-shrink-0"><Form.Control size="sm" autoFocus placeholder="Search in this conversation…" value={search} onChange={(e) => setSearch(e.target.value)} /></div>}
      <div className="chat-body">
        <div className="chat-messages protected" ref={box} onScroll={onScroll} onContextMenu={(e) => e.preventDefault()} onCopy={(e) => e.preventDefault()}>
          <div ref={inner}>
            <div className="chat-system"><FiEyeOff /> Private chat · copying is disabled. Screenshots can't be fully blocked on the web — share sensitive things carefully.</div>
            {loading ? <Loading /> : <>
              {hasMore && msgs.length > 0 && !search && <div className="text-center mb-2"><Button size="sm" variant="link" onClick={() => void loadOlder()}>Load older messages</Button></div>}
              {msgs.length === 0 && <div className="chat-system">{search ? 'No matches.' : 'Say namaste 👋'}</div>}
              {msgs.map((m) => m.type === 'System' ? <div key={m.id} className="chat-system">{m.senderId === me?.id ? 'You' : friend.fullName.split(' ')[0]} {m.body}</div> : (
                <div key={m.id} className={`bubble ${m.senderId === me?.id ? 'mine' : 'theirs'} ${m.local ? 'pending' : ''}`}>
                  {m.replyPreview && <div className="reply">{m.replyPreview}</div>}
                  {m.isDeleted ? <em className="opacity-75">Message deleted</em> : <>
                    {m.local ? m.local.files.map((f, i) => <LocalAttachment key={i} f={f} failed={m.local!.failed} />)
                      : m.attachments.map((a) => <AttachmentView key={a.fileId} m={m} a={a} onPreview={() => setPreview({ url: fileUrl(m.id, a.fileId), name: a.fileName, type: a.contentType })} />)}
                    {m.body && <div style={{ whiteSpace: 'pre-wrap' }}>{m.body}</div>}</>}
                  {m.local?.failed && <div className="small mt-1">Not sent · <span role="button" className="text-decoration-underline" onClick={() => retry(m)}>Retry</span> · <span role="button" className="text-decoration-underline" onClick={() => discard(m)}>Remove</span></div>}
                  <div className="meta d-flex justify-content-end align-items-center gap-1">
                    {m.expiresAt && <FiClock title={`Disappears ${dateTime(m.expiresAt)}`} />}{time(m.createdAt)} {m.senderId === me?.id && !m.isDeleted && tick(m)}
                    {!m.isDeleted && !m.local && <Dropdown className="d-inline"><Dropdown.Toggle as="span" className="no-caret clickable ms-1"><FiMoreVertical /></Dropdown.Toggle><Dropdown.Menu>
                      <Dropdown.Item onClick={() => { setReply(m); input.current?.focus() }}><FiCornerUpLeft className="me-2" />Reply</Dropdown.Item>
                      {m.senderId === me?.id && <Dropdown.Item className="text-danger" onClick={async () => { if (await confirm({ title: 'Delete this message for everyone?' })) await api.delete(`/api/chat/messages/${m.id}`).catch(toast.error) }}><FiTrash2 className="me-2" />Delete</Dropdown.Item>}
                    </Dropdown.Menu></Dropdown>}
                  </div>
                </div>))}
              {typing && <div className="bubble theirs typing-bubble" aria-label={`${friend.fullName} is typing`}><span /><span /><span /></div>}
            </>}
          </div>
        </div>
        {showJump && <Button size="sm" className="chat-jump shadow" onClick={() => { stick.current = true; setShowJump(false); scrollBottom('smooth') }}><FiArrowDown /> New messages</Button>}
      </div>
      {disabled ? <Alert variant="secondary" className="m-2 small flex-shrink-0">{friend.isBlocked ? 'You blocked this user. Unblock to send messages.' : 'You cannot reply to this conversation.'}</Alert> : (
        <Form onSubmit={send} className="chat-composer">
          {reply && <div className="small d-flex justify-content-between bg-body-tertiary rounded p-1 px-2 mb-1"><span className="truncate"><FiCornerUpLeft /> {reply.body ?? 'Attachment'}</span><span role="button" onClick={() => setReply(null)}><FiX /></span></div>}
          {files.length > 0 && <div className="small mb-1 d-flex flex-wrap gap-1">{files.map((f, i) => <span key={i} className="tag">{f.name} · {bytes(f.size)} <span role="button" onClick={() => setFiles(files.filter((_, j) => j !== i))}>✕</span></span>)}</div>}
          <InputGroup>
            <label className="btn btn-outline-secondary mb-0 d-flex align-items-center" title="Attach"><FiPaperclip /><input type="file" hidden multiple accept="image/*,audio/*,video/*,.pdf,.doc,.docx,.xls,.xlsx,.csv,.txt" onChange={(e) => { addFiles(e.target.files); e.target.value = '' }} /></label>
            <Form.Control ref={input} as="textarea" rows={rows} value={text} placeholder="Type a message" onChange={(e) => onType(e.target.value)}
              onPaste={(e) => { if (e.clipboardData.files.length) { e.preventDefault(); addFiles(e.clipboardData.files) } }}
              onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) { e.preventDefault(); send() } }} style={{ resize: 'none' }} />
            <Button type="submit" disabled={!text.trim() && files.length === 0} aria-label="Send"><FiSend /></Button>
          </InputGroup>
        </Form>)}
      <PreviewModal url={preview?.url ?? null} name={preview?.name} contentType={preview?.type} onHide={() => setPreview(null)} />
      <ReportModal show={report} userId={friend.userId} name={friend.fullName} onHide={() => setReport(false)} />
    </>
  )
}


function ReportModal({ show, userId, name, onHide }: { show: boolean; userId: string; name: string; onHide: () => void }) {
  const toast = useToast()
  const [reason, setReason] = useState('Spam')
  const [details, setDetails] = useState('')
  return (
    <Modal show={show} onHide={onHide} centered>
      <Modal.Header closeButton><Modal.Title className="h5">Report {name}</Modal.Title></Modal.Header>
      <Modal.Body><Form.Select className="mb-2" value={reason} onChange={(e) => setReason(e.target.value)}>{['Spam', 'Harassment', 'Fraud / scam', 'Inappropriate content', 'Other'].map((r) => <option key={r}>{r}</option>)}</Form.Select>
        <Form.Control as="textarea" rows={3} placeholder="Details (optional)" value={details} onChange={(e) => setDetails(e.target.value)} /></Modal.Body>
      <Modal.Footer><Button variant="outline-secondary" onClick={onHide}>Cancel</Button><Button variant="danger" onClick={async () => { try { await api.post(`/api/chat/users/${userId}/report`, { reason, details }); toast.success('Report submitted to the NijMitra team'); onHide() } catch (e) { toast.error(e) } }}>Submit report</Button></Modal.Footer>
    </Modal>
  )
}
