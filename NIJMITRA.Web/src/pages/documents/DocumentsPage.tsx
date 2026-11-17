import { useEffect, useMemo, useState } from 'react'
import { Badge, Button, Card, Dropdown, Form, InputGroup, Modal, Spinner, Table } from 'react-bootstrap'
import { useSearchParams } from 'react-router-dom'
import { FiClock, FiCopy, FiDownload, FiEdit2, FiEye, FiFolder, FiFolderPlus, FiLink, FiMoreVertical, FiMove, FiTrash2, FiUpload, FiUploadCloud } from 'react-icons/fi'
import { api, downloadFile, uploadFile } from '../../lib/api'
import { useAsync, useDebounced } from '../../lib/hooks'
import { bytes, date, dateTime, relative } from '../../lib/format'
import { useToast } from '../../contexts/ToastContext'
import { EmptyState, FileDrop, Loading, PageHeader, Pager, PreviewModal, fileIcon, useConfirm } from '../../components/ui'

interface Folder { id: string; name: string; parentId?: string | null; documentCount: number }
interface Doc { id: string; name: string; folderId?: string | null; folderName?: string; category: string; description?: string; tags: string[]; versionCount: number; sizeBytes: number; contentType: string; extension: string; createdAt: string; updatedAt: string }
const CATEGORIES = ['Personal', 'Identity', 'Education', 'Employment', 'Financial', 'Medical', 'Property', 'Business', 'Receipts', 'Other']
const ACCEPT = '.pdf,.doc,.docx,.xls,.xlsx,.csv,.jpg,.jpeg,.png,.webp,.txt'

export default function DocumentsPage() {
  const toast = useToast()
  const confirm = useConfirm()
  const [params, setParams] = useSearchParams()
  const [folder, setFolder] = useState<string | null>(null)
  const [all, setAll] = useState(true)
  const [category, setCategory] = useState('')
  const [q, setQ] = useState('')
  const [sort, setSort] = useState('recent')
  const [page, setPage] = useState(1)
  const search = useDebounced(q)
  const folders = useAsync(() => api.get<Folder[]>('/api/documents/folders').then((r) => r.data), [])
  const docs = useAsync(() => api.get('/api/documents', { params: { folderId: folder ?? undefined, allFolders: all, category: category || undefined, search: search || undefined, sort, page, pageSize: 30 } }).then((r) => r.data as { items: Doc[]; totalPages: number; totalCount: number }), [folder, all, category, search, sort, page])
  const [upload, setUpload] = useState(false)
  const [edit, setEdit] = useState<Doc | null>(null)
  const [versions, setVersions] = useState<Doc | null>(null)
  const [share, setShare] = useState<Doc | null>(null)
  const [preview, setPreview] = useState<Doc | null>(null)
  const [history, setHistory] = useState(false)
  const reload = () => { void docs.reload(); void folders.reload() }

  useEffect(() => {
    if (params.get('new')) { setUpload(true); params.delete('new'); setParams(params, { replace: true }) }
    const open = params.get('open')
    if (open) { api.get<Doc>(`/api/documents/${open}`).then((r) => setPreview(r.data)).catch(() => {}); params.delete('open'); setParams(params, { replace: true }) }
  }, [params, setParams])

  const tree = useMemo(() => {
    const byParent = new Map<string | null, Folder[]>()
    for (const f of folders.data ?? []) { const k = f.parentId ?? null; byParent.set(k, [...(byParent.get(k) ?? []), f]) }
    const render = (parent: string | null, depth: number): React.ReactNode => (byParent.get(parent) ?? []).map((f) => (
      <div key={f.id}>
        <div className={`nm-nav-link py-1 ${!all && folder === f.id ? 'active' : ''}`} style={{ paddingLeft: 12 + depth * 14 }} onClick={() => { setFolder(f.id); setAll(false); setPage(1) }} role="button">
          <FiFolder /><span className="truncate flex-grow-1">{f.name}</span><small>{f.documentCount}</small>
        </div>
        {render(f.id, depth + 1)}
      </div>))
    return render(null, 0)
  }, [folders.data, folder, all])

  const newFolder = async () => {
    const name = prompt('Folder name')
    if (!name?.trim()) return
    try { await api.post('/api/documents/folders', { name: name.trim(), parentId: all ? null : folder }); void folders.reload() } catch (e) { toast.error(e) }
  }
  const currentFolder = folders.data?.find((f) => f.id === folder)

  return (
    <>
      <PageHeader title="Document Vault" subtitle="Your private, organised document locker" icon={<FiFolder />}>
        <Button variant="outline-secondary" size="sm" onClick={() => setHistory(true)}><FiClock /> Upload history</Button>
        <Button size="sm" onClick={() => setUpload(true)}><FiUpload /> Upload</Button>
      </PageHeader>
      <div className="row g-3">
        <div className="col-lg-3">
          <Card><Card.Body className="p-2">
            <div className={`nm-nav-link ${all ? 'active' : ''}`} role="button" onClick={() => { setAll(true); setFolder(null); setPage(1) }}><FiFolder /> All documents</div>
            <div className={`nm-nav-link ${!all && folder === null ? 'active' : ''}`} role="button" onClick={() => { setAll(false); setFolder(null); setPage(1) }}><FiFolder /> Unfiled</div>
            <div className="nm-nav-label d-flex justify-content-between align-items-center">Folders <Button size="sm" variant="link" className="p-0" onClick={() => void newFolder()} title="New folder"><FiFolderPlus /></Button></div>
            {tree}
            {currentFolder && (
              <div className="d-flex gap-1 px-2 mt-2">
                <Button size="sm" variant="outline-secondary" onClick={async () => { const n = prompt('Rename folder', currentFolder.name); if (n?.trim()) { await api.put(`/api/documents/folders/${currentFolder.id}`, { name: n.trim(), parentId: currentFolder.parentId }).catch(toast.error); void folders.reload() } }}><FiEdit2 /> Rename</Button>
                <Button size="sm" variant="outline-danger" onClick={async () => { if (await confirm({ title: `Delete folder "${currentFolder.name}"?`, body: 'Only empty folders can be deleted.' })) { try { await api.delete(`/api/documents/folders/${currentFolder.id}`); setAll(true); setFolder(null); void folders.reload() } catch (e) { toast.error(e) } } }}><FiTrash2 /></Button>
              </div>)}
            <div className="nm-nav-label">Categories</div>
            {CATEGORIES.map((c) => <div key={c} className={`nm-nav-link py-1 ${category === c ? 'active' : ''}`} role="button" onClick={() => { setCategory(category === c ? '' : c); setPage(1) }}>{c}</div>)}
          </Card.Body></Card>
        </div>
        <div className="col-lg-9">
          <Card className="mb-3"><Card.Body className="d-flex flex-wrap gap-2">
            <Form.Control placeholder="Search by name, description or tag…" value={q} onChange={(e) => { setQ(e.target.value); setPage(1) }} style={{ maxWidth: 380 }} />
            <Form.Select value={sort} onChange={(e) => setSort(e.target.value)} style={{ maxWidth: 180 }}><option value="recent">Recently updated</option><option value="name">Name</option><option value="size">Largest</option><option value="oldest">Oldest</option></Form.Select>
            {category && <Badge bg="" className="badge-soft align-self-center clickable" onClick={() => setCategory('')}>{category} ✕</Badge>}
          </Card.Body></Card>
          <Card>
            {docs.loading && !docs.data ? <Loading /> : docs.data?.items.length === 0 ? <EmptyState icon={<FiUploadCloud />} title="No documents here" text="Upload Aadhaar, PAN, certificates, bills and more — stored privately." action={<Button onClick={() => setUpload(true)}><FiUpload /> Upload document</Button>} /> : (
              <Table responsive hover className="mb-0 align-middle">
                <thead><tr><th>Name</th><th>Category</th><th className="d-none d-md-table-cell">Folder</th><th className="d-none d-md-table-cell">Size</th><th>Updated</th><th /></tr></thead>
                <tbody>{docs.data?.items.map((d) => (
                  <tr key={d.id}>
                    <td className="clickable" onClick={() => setPreview(d)}><div className="d-flex align-items-center gap-2"><span className="text-primary fs-5">{fileIcon(d.contentType)}</span><div className="min-w-0"><div className="fw-medium truncate">{d.name}</div>
                      <div className="small text-muted">{d.extension.toUpperCase().slice(1)}{d.versionCount > 1 && ` · v${d.versionCount}`} {d.tags.map((t) => <span key={t} className="tag">#{t}</span>)}</div></div></div></td>
                    <td><Badge bg="" className="badge-soft">{d.category}</Badge></td>
                    <td className="small d-none d-md-table-cell">{d.folderName ?? '—'}</td>
                    <td className="small d-none d-md-table-cell">{bytes(d.sizeBytes)}</td>
                    <td className="small">{relative(d.updatedAt)}</td>
                    <td className="text-end">
                      <Dropdown align="end"><Dropdown.Toggle variant="link" size="sm" className="no-caret text-body"><FiMoreVertical /></Dropdown.Toggle>
                        <Dropdown.Menu>
                          <Dropdown.Item onClick={() => setPreview(d)}><FiEye className="me-2" />Preview</Dropdown.Item>
                          <Dropdown.Item onClick={() => void downloadFile(`/api/documents/${d.id}/download`, d.name)}><FiDownload className="me-2" />Download</Dropdown.Item>
                          <Dropdown.Item onClick={() => setEdit(d)}><FiEdit2 className="me-2" />Rename / edit / move</Dropdown.Item>
                          <Dropdown.Item onClick={() => setVersions(d)}><FiClock className="me-2" />Versions ({d.versionCount})</Dropdown.Item>
                          <Dropdown.Item onClick={() => setShare(d)}><FiLink className="me-2" />Share link</Dropdown.Item>
                          <Dropdown.Divider />
                          <Dropdown.Item className="text-danger" onClick={async () => { if (await confirm({ title: `Delete "${d.name}"?`, body: 'Share links will stop working immediately.' })) { await api.delete(`/api/documents/${d.id}`).catch(toast.error); reload() } }}><FiTrash2 className="me-2" />Delete</Dropdown.Item>
                        </Dropdown.Menu></Dropdown>
                    </td>
                  </tr>))}</tbody>
              </Table>)}
            <Card.Footer className="d-flex justify-content-between align-items-center"><small className="text-muted">{docs.data?.totalCount ?? 0} document(s)</small><Pager page={page} totalPages={docs.data?.totalPages ?? 1} onChange={setPage} /></Card.Footer>
          </Card>
        </div>
      </div>
      {upload && <UploadModal folders={folders.data ?? []} defaultFolder={all ? null : folder} onClose={(ok) => { setUpload(false); if (ok) reload() }} />}
      {edit && <EditModal doc={edit} folders={folders.data ?? []} onClose={(ok) => { setEdit(null); if (ok) reload() }} />}
      {versions && <VersionsModal doc={versions} onClose={() => { setVersions(null); reload() }} />}
      {share && <ShareModal doc={share} onClose={() => setShare(null)} />}
      {history && <HistoryModal onClose={() => setHistory(false)} />}
      <PreviewModal url={preview ? `/api/documents/${preview.id}/download?inline=true` : null} name={preview?.name} contentType={preview?.contentType} onHide={() => setPreview(null)} />
    </>
  )
}

function UploadModal({ folders, defaultFolder, onClose }: { folders: Folder[]; defaultFolder: string | null; onClose: (ok: boolean) => void }) {
  const toast = useToast()
  const [files, setFiles] = useState<File[]>([])
  const [meta, setMeta] = useState({ name: '', category: 'Personal', folderId: defaultFolder ?? '', description: '', tags: '' })
  const [progress, setProgress] = useState<number | null>(null)
  const go = async () => {
    try {
      for (const [i, f] of files.entries()) {
        await uploadFile('/api/documents', f, { name: files.length === 1 && meta.name ? meta.name : f.name.replace(/\.[^.]+$/, ''), category: meta.category, folderId: meta.folderId, description: meta.description, tags: meta.tags },
          (p) => setProgress(Math.round(((i + p / 100) / files.length) * 100)))
      }
      toast.success(`${files.length} document(s) uploaded`); onClose(true)
    } catch (e) { toast.error(e); setProgress(null) }
  }
  return (
    <Modal show onHide={() => onClose(false)} centered size="lg">
      <Modal.Header closeButton><Modal.Title className="h5">Upload documents</Modal.Title></Modal.Header>
      <Modal.Body>
        <FileDrop multiple accept={ACCEPT} onFiles={(f) => setFiles([...files, ...f])} label="PDF, Word, Excel, CSV, images or text — drop here or click" />
        {files.length > 0 && <div className="mt-2">{files.map((f, i) => <div key={i} className="list-row small"><span className="flex-grow-1 truncate">{f.name}</span><span className="text-muted">{bytes(f.size)}</span><Button size="sm" variant="link" className="text-danger p-0" onClick={() => setFiles(files.filter((_, j) => j !== i))}>✕</Button></div>)}</div>}
        <div className="row g-3 mt-1">
          {files.length <= 1 && <Form.Group className="col-md-6"><Form.Label>Document name</Form.Label><Form.Control value={meta.name} onChange={(e) => setMeta({ ...meta, name: e.target.value })} placeholder="e.g. Aadhaar card" /></Form.Group>}
          <Form.Group className="col-md-3"><Form.Label>Category</Form.Label><Form.Select value={meta.category} onChange={(e) => setMeta({ ...meta, category: e.target.value })}>{CATEGORIES.map((c) => <option key={c}>{c}</option>)}</Form.Select></Form.Group>
          <Form.Group className="col-md-3"><Form.Label>Folder</Form.Label><Form.Select value={meta.folderId} onChange={(e) => setMeta({ ...meta, folderId: e.target.value })}><option value="">Unfiled</option>{folders.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}</Form.Select></Form.Group>
          <Form.Group className="col-12"><Form.Label>Tags <small className="text-muted">(comma separated)</small></Form.Label><Form.Control value={meta.tags} onChange={(e) => setMeta({ ...meta, tags: e.target.value })} placeholder="identity, important" /></Form.Group>
          <Form.Group className="col-12"><Form.Label>Description</Form.Label><Form.Control as="textarea" rows={2} value={meta.description} onChange={(e) => setMeta({ ...meta, description: e.target.value })} /></Form.Group>
        </div>
      </Modal.Body>
      <Modal.Footer>{progress !== null && <span className="me-auto small text-muted">Uploading… {progress}%</span>}
        <Button variant="outline-secondary" onClick={() => onClose(false)}>Cancel</Button><Button disabled={files.length === 0 || progress !== null} onClick={() => void go()}>{progress !== null ? <Spinner size="sm" /> : 'Upload'}</Button></Modal.Footer>
    </Modal>
  )
}

function EditModal({ doc, folders, onClose }: { doc: Doc; folders: Folder[]; onClose: (ok: boolean) => void }) {
  const toast = useToast()
  const [m, setM] = useState({ name: doc.name, category: doc.category, folderId: doc.folderId ?? '', description: doc.description ?? '', tags: doc.tags.join(', ') })
  const save = async () => {
    try { await api.put(`/api/documents/${doc.id}`, { ...m, folderId: m.folderId || null, tags: m.tags.split(',').map((t) => t.trim()).filter(Boolean) }); toast.success('Document updated'); onClose(true) } catch (e) { toast.error(e) }
  }
  return (
    <Modal show onHide={() => onClose(false)} centered>
      <Modal.Header closeButton><Modal.Title className="h5"><FiMove className="me-2" />Edit document</Modal.Title></Modal.Header>
      <Modal.Body className="row g-3">
        <Form.Group className="col-12"><Form.Label>Name</Form.Label><Form.Control value={m.name} onChange={(e) => setM({ ...m, name: e.target.value })} /></Form.Group>
        <Form.Group className="col-6"><Form.Label>Category</Form.Label><Form.Select value={m.category} onChange={(e) => setM({ ...m, category: e.target.value })}>{CATEGORIES.map((c) => <option key={c}>{c}</option>)}</Form.Select></Form.Group>
        <Form.Group className="col-6"><Form.Label>Folder</Form.Label><Form.Select value={m.folderId} onChange={(e) => setM({ ...m, folderId: e.target.value })}><option value="">Unfiled</option>{folders.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}</Form.Select></Form.Group>
        <Form.Group className="col-12"><Form.Label>Tags</Form.Label><Form.Control value={m.tags} onChange={(e) => setM({ ...m, tags: e.target.value })} /></Form.Group>
        <Form.Group className="col-12"><Form.Label>Description</Form.Label><Form.Control as="textarea" rows={2} value={m.description} onChange={(e) => setM({ ...m, description: e.target.value })} /></Form.Group>
      </Modal.Body>
      <Modal.Footer><Button variant="outline-secondary" onClick={() => onClose(false)}>Cancel</Button><Button onClick={() => void save()}>Save</Button></Modal.Footer>
    </Modal>
  )
}

function VersionsModal({ doc, onClose }: { doc: Doc; onClose: () => void }) {
  const toast = useToast()
  const v = useAsync(() => api.get(`/api/documents/${doc.id}/versions`).then((r) => r.data as { id: string; versionNumber: number; sizeBytes: number; fileName: string; note?: string; uploadedAt: string }[]), [doc.id])
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const upload = async (files: File[]) => {
    setBusy(true)
    try { await uploadFile(`/api/documents/${doc.id}/versions`, files[0]!, { note }); toast.success('New version uploaded'); setNote(''); void v.reload() } catch (e) { toast.error(e) } finally { setBusy(false) }
  }
  return (
    <Modal show onHide={onClose} centered size="lg">
      <Modal.Header closeButton><Modal.Title className="h5">Versions — {doc.name}</Modal.Title></Modal.Header>
      <Modal.Body>
        <Form.Control className="mb-2" placeholder="Note for the new version (optional)" value={note} onChange={(e) => setNote(e.target.value)} />
        <FileDrop accept={ACCEPT} onFiles={(f) => void upload(f)} busy={busy} label="Upload a new version" />
        <Table className="mt-3 small mb-0"><thead><tr><th>Version</th><th>File</th><th>Size</th><th>Uploaded</th><th /></tr></thead>
          <tbody>{v.data?.map((x) => <tr key={x.id}><td>v{x.versionNumber}</td><td>{x.fileName}{x.note && <div className="text-muted">{x.note}</div>}</td><td>{bytes(x.sizeBytes)}</td><td>{dateTime(x.uploadedAt)}</td>
            <td><Button size="sm" variant="link" onClick={() => void downloadFile(`/api/documents/${doc.id}/download?versionId=${x.id}`, x.fileName)}><FiDownload /></Button></td></tr>)}</tbody></Table>
      </Modal.Body>
    </Modal>
  )
}

function ShareModal({ doc, onClose }: { doc: Doc; onClose: () => void }) {
  const toast = useToast()
  const links = useAsync(() => api.get(`/api/documents/${doc.id}/shares`).then((r) => r.data as { id: string; createdAt: string; expiresAt: string; maxDownloads?: number; downloadCount: number; isActive: boolean }[]), [doc.id])
  const [hours, setHours] = useState(24)
  const [max, setMax] = useState('')
  const [url, setUrl] = useState('')
  const create = async () => {
    try { const r = await api.post(`/api/documents/${doc.id}/shares`, { expiresInHours: hours, maxDownloads: max ? Number(max) : null }); setUrl(r.data.url); void links.reload() } catch (e) { toast.error(e) }
  }
  return (
    <Modal show onHide={onClose} centered>
      <Modal.Header closeButton><Modal.Title className="h5">Share “{doc.name}”</Modal.Title></Modal.Header>
      <Modal.Body>
        <p className="small text-muted">Anyone with the link can download this document until it expires. Revoke it anytime.</p>
        <div className="row g-2 mb-2">
          <div className="col-6"><Form.Label className="small">Expires after</Form.Label><Form.Select value={hours} onChange={(e) => setHours(Number(e.target.value))}><option value={1}>1 hour</option><option value={24}>1 day</option><option value={72}>3 days</option><option value={168}>7 days</option><option value={720}>30 days</option></Form.Select></div>
          <div className="col-6"><Form.Label className="small">Max downloads</Form.Label><Form.Control type="number" min={1} placeholder="Unlimited" value={max} onChange={(e) => setMax(e.target.value)} /></div>
        </div>
        <Button onClick={() => void create()}><FiLink /> Create link</Button>
        {url && <InputGroup className="mt-3"><Form.Control readOnly value={url} /><Button variant="outline-primary" onClick={() => { void navigator.clipboard.writeText(url); toast.success('Link copied') }}><FiCopy /></Button></InputGroup>}
        {url && <div className="small text-warning mt-1">Copy this link now — for security it is shown only once.</div>}
        <h6 className="fw-bold mt-4">Existing links</h6>
        {links.data?.length === 0 && <div className="small text-muted">No links yet.</div>}
        {links.data?.map((l) => <div key={l.id} className="list-row small"><div className="flex-grow-1">Created {dateTime(l.createdAt)} · expires {dateTime(l.expiresAt)} · {l.downloadCount}{l.maxDownloads ? `/${l.maxDownloads}` : ''} downloads</div>
          {l.isActive ? <Button size="sm" variant="outline-danger" onClick={() => void api.delete(`/api/documents/shares/${l.id}`).then(() => links.reload())}>Revoke</Button> : <Badge bg="secondary">Inactive</Badge>}</div>)}
      </Modal.Body>
    </Modal>
  )
}

function HistoryModal({ onClose }: { onClose: () => void }) {
  const h = useAsync(() => api.get('/api/documents/history', { params: { take: 100 } }).then((r) => r.data as { documentId: string; documentName: string; versionNumber: number; sizeBytes: number; uploadedAt: string }[]), [])
  return (
    <Modal show onHide={onClose} centered size="lg">
      <Modal.Header closeButton><Modal.Title className="h5">Upload history</Modal.Title></Modal.Header>
      <Modal.Body>{h.loading ? <Loading /> : <Table size="sm" className="small"><thead><tr><th>Document</th><th>Version</th><th>Size</th><th>Uploaded</th></tr></thead>
        <tbody>{h.data?.map((x, i) => <tr key={i}><td>{x.documentName}</td><td>v{x.versionNumber}</td><td>{bytes(x.sizeBytes)}</td><td>{dateTime(x.uploadedAt)} <span className="text-muted">({date(x.uploadedAt)})</span></td></tr>)}</tbody></Table>}</Modal.Body>
    </Modal>
  )
}
