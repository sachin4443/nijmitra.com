import { useEffect, useState } from 'react'
import { Badge, Button, Card, Form, Spinner } from 'react-bootstrap'
import { EditorContent, useEditor } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import { useNavigate, useParams } from 'react-router-dom'
import { FiArrowLeft, FiBold, FiBookOpen, FiCode, FiEdit2, FiImage, FiItalic, FiList, FiPaperclip, FiPlus, FiTrash2 } from 'react-icons/fi'
import { api, blobUrl, uploadFile } from '../../lib/api'
import { useAsync, useDebounced } from '../../lib/hooks'
import { date } from '../../lib/format'
import { useToast } from '../../contexts/ToastContext'
import { AttachmentList, EmptyState, Loading, PageHeader, Pager, PreviewModal, type FileInfo, useConfirm } from '../../components/ui'

const MOODS: Record<string, string> = { happy: '😊', grateful: '🙏', calm: '😌', excited: '🤩', neutral: '😐', tired: '😴', sad: '😢', anxious: '😟', angry: '😠' }
interface Item { id: string; title: string; entryDate: string; entryTime: string; mood?: string; tags: string[]; excerpt: string; attachmentCount: number; coverPhotoId?: string }
interface Entry { id: string; title: string; contentHtml: string; entryDate: string; entryTime: string; mood?: string; tags: string[]; attachments: (FileInfo & { isPhoto: boolean })[]; createdAt: string; updatedAt: string }

export default function DiaryPage() {
  const { id } = useParams()
  if (id === 'new') return <DiaryEditor />
  if (id) return <DiaryView id={id} />
  return <DiaryList />
}

function Photo({ fileId }: { fileId: string }) {
  const [src, setSrc] = useState<string>()
  useEffect(() => { let u: string | undefined; blobUrl(`/api/files/${fileId}?inline=true`).then((x) => { u = x; setSrc(x) }).catch(() => {}); return () => { if (u) URL.revokeObjectURL(u) } }, [fileId])
  return src ? <img src={src} alt="" style={{ width: '100%', height: 140, objectFit: 'cover', borderRadius: '12px 12px 0 0' }} /> : null
}

function DiaryList() {
  const nav = useNavigate()
  const [q, setQ] = useState('')
  const search = useDebounced(q)
  const [mood, setMood] = useState('')
  const [tag, setTag] = useState('')
  const [month, setMonth] = useState<{ year: number; month: number } | null>(null)
  const [page, setPage] = useState(1)
  const archive = useAsync(() => api.get('/api/diary/archive').then((r) => r.data as { year: number; month: number; count: number }[]), [])
  const tags = useAsync(() => api.get<string[]>('/api/diary/tags').then((r) => r.data), [])
  const range = month ? { from: `${month.year}-${String(month.month).padStart(2, '0')}-01`, to: new Date(month.year, month.month, 0).toISOString().slice(0, 10) } : {}
  const list = useAsync(() => api.get('/api/diary', { params: { search: search || undefined, mood: mood || undefined, tag: tag || undefined, ...range, page, pageSize: 12 } }).then((r) => r.data as { items: Item[]; totalPages: number; totalCount: number }), [search, mood, tag, month?.year, month?.month, page])
  const years = Array.from(new Set(archive.data?.map((a) => a.year)))
  return (
    <>
      <PageHeader title="Daily Diary" subtitle="अपने विचार, अनुभव और रोज़ की बातें — private & encrypted" icon={<FiBookOpen />}><Button onClick={() => nav('/diary/new')}><FiPlus /> New entry</Button></PageHeader>
      <div className="row g-3">
        <div className="col-lg-3">
          <Card className="mb-3"><Card.Body className="p-2">
            <div className={`nm-nav-link ${!month ? 'active' : ''}`} role="button" onClick={() => setMonth(null)}>Timeline (all)</div>
            {years.map((y) => <div key={y}><div className="nm-nav-label">{y} · {archive.data?.filter((a) => a.year === y).reduce((s, a) => s + a.count, 0)} entries</div>
              {archive.data?.filter((a) => a.year === y).map((a) => <div key={a.month} role="button" className={`nm-nav-link py-1 ${month?.year === y && month.month === a.month ? 'active' : ''}`} onClick={() => { setMonth({ year: y, month: a.month }); setPage(1) }}>
                <span className="flex-grow-1">{new Date(y, a.month - 1).toLocaleString('en-IN', { month: 'long' })}</span><small>{a.count}</small></div>)}</div>)}
          </Card.Body></Card>
          <Card><Card.Body>
            <div className="small fw-semibold mb-2">Mood</div>
            <div className="mb-3">{Object.entries(MOODS).map(([k, e]) => <span key={k} title={k} role="button" className={`fs-4 me-1 ${mood === k ? '' : 'opacity-50'}`} onClick={() => setMood(mood === k ? '' : k)}>{e}</span>)}</div>
            <div className="small fw-semibold mb-2">Tags</div>
            {tags.data?.length === 0 && <div className="small text-muted">No tags yet.</div>}
            {tags.data?.map((t) => <span key={t} role="button" className={`tag ${tag === t ? 'bg-primary text-white' : ''}`} onClick={() => setTag(tag === t ? '' : t)}>#{t}</span>)}
          </Card.Body></Card>
        </div>
        <div className="col-lg-9">
          <Form.Control className="mb-3" placeholder="Search titles, tags or moods…" value={q} onChange={(e) => setQ(e.target.value)} />
          {list.loading && !list.data ? <Loading /> : list.data?.items.length === 0 ? <Card><EmptyState icon={<FiBookOpen />} title="No entries" text="आज का दिन कैसा रहा? Write your first entry." action={<Button onClick={() => nav('/diary/new')}><FiPlus /> Write</Button>} /></Card> : (
            <div className="row g-3">{list.data?.items.map((e) => (
              <div className="col-md-6 col-xl-4" key={e.id}><Card className="h-100 clickable" onClick={() => nav(`/diary/${e.id}`)}>
                {e.coverPhotoId && <Photo fileId={e.coverPhotoId} />}
                <Card.Body>
                  <div className="d-flex justify-content-between small text-muted mb-1"><span>{date(e.entryDate)} · {e.entryTime.slice(0, 5)}</span><span className="fs-5">{e.mood ? MOODS[e.mood] : ''}</span></div>
                  <h6 className="fw-bold">{e.title}</h6>
                  <p className="small text-muted mb-2">{e.excerpt}</p>
                  {e.tags.map((t) => <span key={t} className="tag">#{t}</span>)} {e.attachmentCount > 0 && <Badge bg="" className="badge-soft"><FiPaperclip /> {e.attachmentCount}</Badge>}
                </Card.Body></Card></div>))}</div>)}
          <div className="mt-3"><Pager page={page} totalPages={list.data?.totalPages ?? 1} onChange={setPage} /></div>
        </div>
      </div>
    </>
  )
}

function DiaryView({ id }: { id: string }) {
  const nav = useNavigate()
  const confirm = useConfirm()
  const e = useAsync(() => api.get<Entry>(`/api/diary/${id}`).then((r) => r.data), [id])
  const [editing, setEditing] = useState(false)
  const [preview, setPreview] = useState<FileInfo | null>(null)
  if (e.loading && !e.data) return <Loading />
  if (!e.data) return null
  if (editing) return <DiaryEditor entry={e.data} onDone={() => { setEditing(false); void e.reload() }} />
  const d = e.data
  return (
    <>
      <PageHeader title={d.title} subtitle={`${date(d.entryDate)} · ${d.entryTime.slice(0, 5)} ${d.mood ? `· ${MOODS[d.mood]} ${d.mood}` : ''}`}>
        <Button size="sm" variant="outline-secondary" onClick={() => nav('/diary')}><FiArrowLeft /> Diary</Button>
        <Button size="sm" onClick={() => setEditing(true)}><FiEdit2 /> Edit</Button>
        <Button size="sm" variant="outline-danger" onClick={async () => { if (await confirm({ title: 'Delete this entry?' })) { await api.delete(`/api/diary/${id}`); nav('/diary') } }}><FiTrash2 /></Button>
      </PageHeader>
      <Card className="mb-3"><Card.Body>
        <div className="diary-content" dangerouslySetInnerHTML={{ __html: d.contentHtml /* sanitised server-side */ }} />
        <div className="mt-3">{d.tags.map((t) => <span key={t} className="tag">#{t}</span>)}</div>
      </Card.Body></Card>
      {d.attachments.length > 0 && <Card><Card.Header>Photos & attachments</Card.Header><Card.Body>
        <div className="row g-2 mb-2">{d.attachments.filter((a) => a.isPhoto).map((a) => <div className="col-6 col-md-3 clickable" key={a.fileId} onClick={() => setPreview(a)}><Photo fileId={a.fileId!} /></div>)}</div>
        <AttachmentList files={d.attachments.filter((a) => !a.isPhoto)} urlFor={(f) => `/api/files/${f.fileId}`} onPreview={setPreview} />
      </Card.Body></Card>}
      <PreviewModal url={preview ? `/api/files/${preview.fileId}?inline=true` : null} name={preview?.name} contentType={preview?.contentType} onHide={() => setPreview(null)} />
    </>
  )
}

function DiaryEditor({ entry, onDone }: { entry?: Entry; onDone?: () => void }) {
  const nav = useNavigate()
  const toast = useToast()
  const now = new Date()
  const [title, setTitle] = useState(entry?.title ?? '')
  const [entryDate, setDate] = useState(entry?.entryDate ?? new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 10))
  const [entryTime, setTime] = useState(entry?.entryTime.slice(0, 5) ?? now.toTimeString().slice(0, 5))
  const [mood, setMood] = useState(entry?.mood ?? '')
  const [tags, setTags] = useState(entry?.tags.join(', ') ?? '')
  const [atts, setAtts] = useState(entry?.attachments ?? [])
  const [pending, setPending] = useState<File[]>([])
  const [busy, setBusy] = useState(false)
  const editor = useEditor({ extensions: [StarterKit], content: entry?.contentHtml ?? '<p></p>' })
  const save = async () => {
    if (!editor) return
    setBusy(true)
    try {
      const body = { title, contentHtml: editor.getHTML(), entryDate, entryTime: `${entryTime}:00`, mood: mood || null, tags: tags.split(',').map((t) => t.trim()).filter(Boolean) }
      const r = entry ? await api.put<Entry>(`/api/diary/${entry.id}`, body) : await api.post<Entry>('/api/diary', body)
      for (const f of pending) await uploadFile(`/api/diary/${r.data.id}/attachments`, f)
      toast.success('Entry saved')
      if (onDone) onDone(); else nav(`/diary/${r.data.id}`)
    } catch (x) { toast.error(x) } finally { setBusy(false) }
  }
  const tb = (active: boolean, onClick: () => void, icon: React.ReactNode, label: string) => <Button size="sm" variant={active ? 'primary' : 'outline-secondary'} onClick={onClick} title={label} aria-label={label}>{icon}</Button>
  return (
    <>
      <PageHeader title={entry ? 'Edit entry' : 'New diary entry'}>
        <Button size="sm" variant="outline-secondary" onClick={() => (onDone ? onDone() : nav('/diary'))}>Cancel</Button>
        <Button size="sm" disabled={busy || !title.trim()} onClick={() => void save()}>{busy ? <Spinner size="sm" /> : 'Save entry'}</Button>
      </PageHeader>
      <Card><Card.Body className="row g-3">
        <Form.Group className="col-md-6"><Form.Control size="lg" placeholder="Title — आज का दिन…" value={title} onChange={(e) => setTitle(e.target.value)} /></Form.Group>
        <Form.Group className="col-6 col-md-3"><Form.Control type="date" value={entryDate} onChange={(e) => setDate(e.target.value)} /></Form.Group>
        <Form.Group className="col-6 col-md-3"><Form.Control type="time" value={entryTime} onChange={(e) => setTime(e.target.value)} /></Form.Group>
        <div className="col-12">
          {editor && <div className="editor-toolbar">
            {tb(editor.isActive('bold'), () => editor.chain().focus().toggleBold().run(), <FiBold />, 'Bold')}
            {tb(editor.isActive('italic'), () => editor.chain().focus().toggleItalic().run(), <FiItalic />, 'Italic')}
            {tb(editor.isActive('heading', { level: 2 }), () => editor.chain().focus().toggleHeading({ level: 2 }).run(), <strong>H</strong>, 'Heading')}
            {tb(editor.isActive('bulletList'), () => editor.chain().focus().toggleBulletList().run(), <FiList />, 'Bullet list')}
            {tb(editor.isActive('orderedList'), () => editor.chain().focus().toggleOrderedList().run(), <span>1.</span>, 'Numbered list')}
            {tb(editor.isActive('blockquote'), () => editor.chain().focus().toggleBlockquote().run(), <span>❝</span>, 'Quote')}
            {tb(editor.isActive('codeBlock'), () => editor.chain().focus().toggleCodeBlock().run(), <FiCode />, 'Code')}
          </div>}
          <EditorContent editor={editor} />
        </div>
        <div className="col-md-6"><div className="small fw-semibold mb-1">Mood</div>{Object.entries(MOODS).map(([k, e]) => <span key={k} title={k} role="button" className={`fs-3 me-1 ${mood === k ? '' : 'opacity-50'}`} onClick={() => setMood(mood === k ? '' : k)}>{e}</span>)}</div>
        <Form.Group className="col-md-6"><Form.Label className="small fw-semibold">Tags</Form.Label><Form.Control placeholder="family, travel, work" value={tags} onChange={(e) => setTags(e.target.value)} /></Form.Group>
        <div className="col-12">
          <label className="btn btn-sm btn-outline-primary"><FiImage /> Add photos / files<input type="file" hidden multiple accept="image/*,.pdf,.doc,.docx,.txt" onChange={(e) => setPending([...pending, ...Array.from(e.target.files ?? [])])} /></label>
          {pending.map((f, i) => <span key={i} className="tag ms-2">{f.name} <span role="button" onClick={() => setPending(pending.filter((_, j) => j !== i))}>✕</span></span>)}
          {atts.length > 0 && <div className="mt-2"><AttachmentList files={atts} urlFor={(f) => `/api/files/${f.fileId}`} onRemove={async (f) => { await api.delete(`/api/diary/${entry!.id}/attachments/${f.fileId}`); setAtts(atts.filter((a) => a.fileId !== f.fileId)) }} /></div>}
        </div>
      </Card.Body></Card>
    </>
  )
}
