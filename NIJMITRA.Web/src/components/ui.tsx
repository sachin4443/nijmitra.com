import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import { Badge, Button, Dropdown, Form, Modal, Pagination as BsPagination, Spinner } from 'react-bootstrap'
import { FiDownload, FiFile, FiFileText, FiImage, FiInbox, FiPaperclip, FiTrash2, FiUploadCloud } from 'react-icons/fi'
import { Link } from 'react-router-dom'
import { api, apiError, blobUrl, downloadFile, uploadFile } from '../lib/api'
import { bytes, initials } from '../lib/format'
import { useTheme } from '../contexts/ThemeContext'

/** Official NijMitra logo (supplied artwork, transparent). Uses the light-text variant on dark backgrounds. */
export function Logo({ height = 40, variant, stacked = false }: { height?: number; variant?: 'light' | 'dark'; stacked?: boolean }) {
  const { resolved } = useTheme()
  const v = variant ?? resolved
  const file = `${stacked ? 'logo-stacked' : 'logo'}-${v === 'dark' ? 'dark' : stacked ? '' : 'light'}`.replace(/-$/, '')
  return <img src={`/branding/${file}.webp`} alt="NijMitra — Aapka Personal Digital Saathi" height={height} style={{ display: 'block', width: 'auto', maxWidth: '100%' }} />
}

export function Mark({ size = 36 }: { size?: number }) {
  return <img src="/branding/mark.webp" alt="NijMitra" height={size} style={{ width: 'auto', display: 'block' }} />
}

/** Avatar that loads protected images through the authenticated client. */
export function Avatar({ name, src, size = 36 }: { name?: string; src?: string | null; size?: number }) {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    let revoke: string | null = null
    if (src) blobUrl(src).then((u) => { revoke = u; setUrl(u) }).catch(() => setUrl(null))
    else setUrl(null)
    return () => { if (revoke) URL.revokeObjectURL(revoke) }
  }, [src])
  return <span className="avatar" style={{ width: size, height: size, fontSize: size * 0.38 }}>{url ? <img src={url} alt={name} /> : initials(name)}</span>
}

export function PageHeader({ title, subtitle, icon, children }: { title: string; subtitle?: string; icon?: ReactNode; children?: ReactNode }) {
  return (
    <div className="page-header">
      <div className="d-flex align-items-center gap-3">
        {icon && <span className="stat-card p-0"><span className="icon badge-soft">{icon}</span></span>}
        <div><h1>{title}</h1>{subtitle && <div className="text-muted small">{subtitle}</div>}</div>
      </div>
      <div className="d-flex flex-wrap gap-2">{children}</div>
    </div>
  )
}

export function StatCard({ label, value, icon, tone = 'primary', hint, onClick }: { label: string; value: ReactNode; icon: ReactNode; tone?: 'primary' | 'success' | 'danger' | 'warning' | 'info'; hint?: ReactNode; onClick?: () => void }) {
  const cls = { primary: 'badge-soft', success: 'bg-soft-success', danger: 'bg-soft-danger', warning: 'bg-soft-warning', info: 'bg-soft-info' }[tone]
  return (
    <div className={`nm-card stat-card ${onClick ? 'clickable' : ''}`} onClick={onClick}>
      <span className={`icon ${cls}`}>{icon}</span>
      <div className="min-w-0"><div className="label">{label}</div><div className="value">{value}</div>{hint && <div className="small text-muted">{hint}</div>}</div>
    </div>
  )
}

export function EmptyState({ icon = <FiInbox />, title, text, action }: { icon?: ReactNode; title: string; text?: string; action?: ReactNode }) {
  return <div className="empty-state">{icon}<h6 className="fw-semibold text-body">{title}</h6>{text && <p className="small mb-3">{text}</p>}{action}</div>
}

export const Loading = ({ text }: { text?: string }) => <div className="text-center py-5 text-muted"><Spinner animation="border" size="sm" className="me-2" />{text ?? 'Loading…'}</div>

export function ErrorBox({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  return <div className="alert alert-danger d-flex justify-content-between align-items-center"><span>{apiError(error).message}</span>{onRetry && <Button size="sm" variant="outline-danger" onClick={onRetry}>Retry</Button>}</div>
}

export function Pager({ page, totalPages, onChange }: { page: number; totalPages: number; onChange: (p: number) => void }) {
  if (totalPages <= 1) return null
  const pages = Array.from(new Set([1, page - 1, page, page + 1, totalPages].filter((p) => p >= 1 && p <= totalPages))).sort((a, b) => a - b)
  return (
    <BsPagination size="sm" className="mb-0 justify-content-end">
      <BsPagination.Prev disabled={page <= 1} onClick={() => onChange(page - 1)} />
      {pages.map((p, i) => (
        <span key={p} className="d-flex">{i > 0 && pages[i - 1]! < p - 1 && <BsPagination.Ellipsis disabled />}<BsPagination.Item active={p === page} onClick={() => onChange(p)}>{p}</BsPagination.Item></span>
      ))}
      <BsPagination.Next disabled={page >= totalPages} onClick={() => onChange(page + 1)} />
    </BsPagination>
  )
}

// ---------------------------------------------------------------- confirm dialog
interface ConfirmOpts { title: string; body?: ReactNode; confirmText?: string; variant?: string }
const ConfirmCtx = createContext<(o: ConfirmOpts) => Promise<boolean>>(async () => false)
export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [opts, setOpts] = useState<ConfirmOpts | null>(null)
  const resolver = useRef<(v: boolean) => void>(() => {})
  const confirm = useCallback((o: ConfirmOpts) => new Promise<boolean>((res) => { resolver.current = res; setOpts(o) }), [])
  const close = (v: boolean) => { resolver.current(v); setOpts(null) }
  return (
    <ConfirmCtx.Provider value={confirm}>
      {children}
      <Modal show={!!opts} onHide={() => close(false)} centered>
        <Modal.Header closeButton><Modal.Title className="h5">{opts?.title}</Modal.Title></Modal.Header>
        {opts?.body && <Modal.Body>{opts.body}</Modal.Body>}
        <Modal.Footer>
          <Button variant="outline-secondary" onClick={() => close(false)}>Cancel</Button>
          <Button variant={opts?.variant ?? 'danger'} onClick={() => close(true)}>{opts?.confirmText ?? 'Delete'}</Button>
        </Modal.Footer>
      </Modal>
    </ConfirmCtx.Provider>
  )
}
export const useConfirm = () => useContext(ConfirmCtx)

// ---------------------------------------------------------------- re-authentication (PIN)
const ReauthCtx = createContext<() => Promise<boolean>>(async () => false)
export function ReauthProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false)
  const [pin, setPin] = useState('')
  const [usePassword, setUsePassword] = useState(false)
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  const resolver = useRef<(v: boolean) => void>(() => {})
  const ask = useCallback(() => new Promise<boolean>((res) => { resolver.current = res; setPin(''); setErr(''); setOpen(true) }), [])
  const close = (v: boolean) => { resolver.current(v); setOpen(false); setPin('') }
  const submit = async () => {
    setBusy(true); setErr('')
    try { await api.post('/api/auth/reauthenticate', usePassword ? { password: pin } : { pin }); close(true) }
    catch (e) { setErr(apiError(e).message) }
    finally { setBusy(false) }
  }
  return (
    <ReauthCtx.Provider value={ask}>
      {children}
      <Modal show={open} onHide={() => close(false)} centered size="sm">
        <Modal.Body className="p-4">
          <h5 className="fw-bold">Confirm it's you</h5>
          <p className="text-muted small">This action needs your {usePassword ? 'account password' : 'security PIN'}.</p>
          <Form onSubmit={(e) => { e.preventDefault(); void submit() }}>
            <Form.Control autoFocus type="password" inputMode={usePassword ? 'text' : 'numeric'} autoComplete="off" value={pin} onChange={(e) => setPin(e.target.value)} placeholder={usePassword ? 'Password' : '••••'} isInvalid={!!err} />
            <Form.Control.Feedback type="invalid">{err}</Form.Control.Feedback>
            <div className="d-grid gap-2 mt-3">
              <Button type="submit" disabled={busy || !pin}>{busy ? <Spinner size="sm" /> : 'Confirm'}</Button>
              <Button variant="link" size="sm" onClick={() => { setUsePassword(!usePassword); setPin(''); setErr('') }}>Use {usePassword ? 'PIN' : 'password'} instead</Button>
            </div>
          </Form>
        </Modal.Body>
      </Modal>
    </ReauthCtx.Provider>
  )
}
export const useReauth = () => useContext(ReauthCtx)

/** Runs an action; if the server answers reauth_required, asks for the PIN and retries once. */
export async function withReauth<T>(ask: () => Promise<boolean>, action: () => Promise<T>): Promise<T | undefined> {
  try { return await action() }
  catch (e) {
    if (apiError(e).code !== 'reauth_required') throw e
    if (!(await ask())) return undefined
    return await action()
  }
}

// ---------------------------------------------------------------- files
export function FileDrop({ onFiles, accept, multiple, label, busy }: { onFiles: (f: File[]) => void; accept?: string; multiple?: boolean; label?: string; busy?: boolean }) {
  const input = useRef<HTMLInputElement>(null)
  const [drag, setDrag] = useState(false)
  return (
    <div className={`dropzone ${drag ? 'drag' : ''}`} onClick={() => input.current?.click()}
      onDragOver={(e) => { e.preventDefault(); setDrag(true) }} onDragLeave={() => setDrag(false)}
      onDrop={(e) => { e.preventDefault(); setDrag(false); if (e.dataTransfer.files.length) onFiles(Array.from(e.dataTransfer.files)) }}>
      {busy ? <Spinner size="sm" /> : <FiUploadCloud size={26} />}
      <div className="small mt-1">{label ?? 'Drop files here or click to browse'}</div>
      <input ref={input} type="file" hidden accept={accept} multiple={multiple} onChange={(e) => { if (e.target.files?.length) onFiles(Array.from(e.target.files)); e.target.value = '' }} />
    </div>
  )
}

export interface FileInfo { id?: string; fileId?: string; name?: string; fileName?: string; contentType: string; sizeBytes: number }

export function fileIcon(ct: string) {
  if (ct.startsWith('image/')) return <FiImage />
  if (ct === 'application/pdf' || ct.startsWith('text/')) return <FiFileText />
  return <FiFile />
}

export function AttachmentList({ files, urlFor, onRemove, onPreview }: { files: FileInfo[]; urlFor: (f: FileInfo) => string; onRemove?: (f: FileInfo) => void; onPreview?: (f: FileInfo) => void }) {
  if (files.length === 0) return <div className="small text-muted"><FiPaperclip /> No attachments</div>
  return (
    <div>
      {files.map((f) => {
        const name = f.name ?? f.fileName ?? 'file'
        return (
          <div key={f.id ?? f.fileId} className="list-row">
            <span className="text-primary">{fileIcon(f.contentType)}</span>
            <span className="truncate flex-grow-1 clickable" onClick={() => onPreview?.(f)}>{name}</span>
            <span className="small text-muted">{bytes(f.sizeBytes)}</span>
            <Button size="sm" variant="link" onClick={() => void downloadFile(urlFor(f), name)} title="Download"><FiDownload /></Button>
            {onRemove && <Button size="sm" variant="link" className="text-danger" onClick={() => onRemove(f)} title="Remove"><FiTrash2 /></Button>}
          </div>
        )
      })}
    </div>
  )
}

/** Previews PDFs, images, audio, video and text inline (loaded via authenticated blob URL). */
export function PreviewModal({ url, name, contentType, onHide }: { url: string | null; name?: string; contentType?: string; onHide: () => void }) {
  const [src, setSrc] = useState<string | null>(null)
  const [text, setText] = useState<string | null>(null)
  useEffect(() => {
    let created: string | null = null
    setSrc(null); setText(null)
    if (!url) return
    if (contentType?.startsWith('text/')) api.get(url, { responseType: 'text' }).then((r) => setText(String(r.data))).catch(() => setText('Unable to load preview.'))
    else blobUrl(url).then((u) => { created = u; setSrc(u) }).catch(() => setText('Unable to load preview.'))
    return () => { if (created) URL.revokeObjectURL(created) }
  }, [url, contentType])
  const canPreview = contentType && (contentType === 'application/pdf' || /^(image|audio|video|text)\//.test(contentType))
  return (
    <Modal show={!!url} onHide={onHide} size="xl" centered>
      <Modal.Header closeButton><Modal.Title className="h6 truncate">{name}</Modal.Title></Modal.Header>
      <Modal.Body className="p-0" style={{ minHeight: 300 }}>
        {!canPreview && <EmptyState icon={<FiFile />} title="Preview not available" text="Download the file to open it in its app (Word, Excel…)." action={url ? <Button onClick={() => void downloadFile(url, name)}><FiDownload /> Download</Button> : undefined} />}
        {canPreview && !src && text === null && <Loading />}
        {text !== null && <pre className="p-3 mb-0" style={{ whiteSpace: 'pre-wrap', maxHeight: '75vh' }}>{text}</pre>}
        {src && contentType === 'application/pdf' && <iframe title={name} src={src} style={{ width: '100%', height: '78vh', border: 0 }} />}
        {src && contentType?.startsWith('image/') && <img src={src} alt={name} style={{ maxWidth: '100%', maxHeight: '78vh', display: 'block', margin: '0 auto' }} />}
        {src && contentType?.startsWith('audio/') && <div className="p-4"><audio src={src} controls className="w-100" /></div>}
        {src && contentType?.startsWith('video/') && <video src={src} controls style={{ width: '100%', maxHeight: '78vh' }} />}
      </Modal.Body>
    </Modal>
  )
}

export function ExportMenu({ url, formats = ['xlsx', 'csv', 'pdf'], label = 'Export', size = 'sm' }: { url: (format: string) => string; formats?: string[]; label?: string; size?: 'sm' | 'lg' }) {
  const names: Record<string, string> = { xlsx: 'Excel (.xlsx)', csv: 'CSV (.csv)', pdf: 'PDF (.pdf)' }
  return (
    <Dropdown>
      <Dropdown.Toggle variant="outline-secondary" size={size} className="btn-icon"><FiDownload /> {label}</Dropdown.Toggle>
      <Dropdown.Menu align="end">{formats.map((f) => <Dropdown.Item key={f} onClick={() => void downloadFile(url(f), `export.${f}`)}>{names[f]}</Dropdown.Item>)}</Dropdown.Menu>
    </Dropdown>
  )
}

export async function uploadMany(url: string, files: File[], fields?: Record<string, string>) {
  const out = []
  for (const f of files) out.push(await uploadFile(url, f, fields))
  return out
}

export const StatusBadge = ({ status }: { status: string }) => {
  const map: Record<string, string> = { Active: 'bg-soft-success', Paid: 'bg-soft-success', Settled: 'bg-soft-success', Sent: 'bg-soft-success', Pending: 'bg-soft-warning', Open: 'bg-soft-warning', PartiallySettled: 'bg-soft-info', Processing: 'bg-soft-info', Overdue: 'bg-soft-danger', Failed: 'bg-soft-danger', Dead: 'bg-soft-danger', Suspended: 'bg-soft-danger', Inactive: 'bg-soft-warning', PendingDeletion: 'bg-soft-danger', Skipped: 'badge-soft' }
  return <Badge bg="" className={map[status] ?? 'badge-soft'}>{status.replace(/([a-z])([A-Z])/g, '$1 $2')}</Badge>
}

export const Highlight = ({ text, term }: { text: string; term: string }) => {
  if (!term || !text) return <>{text}</>
  const parts = text.split(new RegExp(`(${term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'gi'))
  return <>{parts.map((p, i) => (p.toLowerCase() === term.toLowerCase() ? <mark key={i}>{p}</mark> : <span key={i}>{p}</span>))}</>
}

/** Router link styled as a Bootstrap button (keeps SPA navigation). */
export function LinkButton({ to, variant = 'primary', size, className = '', children }: { to: string; variant?: string; size?: 'sm' | 'lg'; className?: string; children: ReactNode }) {
  return <Link to={to} className={`btn btn-${variant} ${size ? `btn-${size}` : ''} ${className}`.trim()}>{children}</Link>
}
