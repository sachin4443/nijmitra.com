import { useEffect, useMemo, useState } from 'react'
import { Alert, Badge, Button, Card, Dropdown, Form, InputGroup, Modal, Nav, Spinner } from 'react-bootstrap'
import { useSearchParams } from 'react-router-dom'
import { FiClock, FiCopy, FiDownload, FiEdit2, FiExternalLink, FiEye, FiEyeOff, FiFileText, FiKey, FiLock, FiPlus, FiRefreshCw, FiShield, FiStar, FiTrash2, FiUnlock, FiUpload } from 'react-icons/fi'
import { api, apiError } from '../../lib/api'
import { useAsync, useDebounced } from '../../lib/hooks'
import { dateTime, relative } from '../../lib/format'
import { createVault, decryptJson, decryptText, encryptJson, encryptText, generatePassword, passwordStrength, rekeyVault, unlockVault, type VaultHeader } from '../../lib/vaultCrypto'
import { useVault } from '../../contexts/VaultContext'
import { useToast } from '../../contexts/ToastContext'
import { EmptyState, Loading, PageHeader, useConfirm, useReauth, withReauth } from '../../components/ui'

interface Entry { id: string; title: string; websiteUrl?: string; applicationName?: string; category: string; tags?: string; isFavorite: boolean; encryptedData: string; updatedAt: string; createdAt: string }
interface Secret { username?: string; email?: string; password?: string; notes?: string }
interface Note { id: string; encryptedData: string; isFavorite: boolean; updatedAt: string }

/** Copies to the clipboard and clears it again after 30 s (if it still holds our value). */
async function copySecret(value: string | undefined, toast: ReturnType<typeof useToast>, label: string) {
  if (!value) return
  await navigator.clipboard.writeText(value)
  toast.success(`${label} copied — clipboard clears in 30 s`)
  setTimeout(async () => { try { if ((await navigator.clipboard.readText()) === value) await navigator.clipboard.writeText('') } catch { /* permission denied: best effort */ } }, 30000)
}

export default function PasswordsPage() {
  const { key } = useVault()
  const header = useAsync(() => api.get('/api/vault').then((r) => r.data as { configured: boolean; header: VaultHeader | null }), [])
  if (header.loading) return <Loading />
  if (header.error) return <Alert variant="danger">{apiError(header.error).message}</Alert>
  return (
    <>
      <PageHeader title="Password Manager" subtitle="Zero-knowledge vault — encrypted on this device with your master password" icon={<FiShield />} />
      {!header.data?.configured ? <VaultSetup onDone={() => void header.reload()} /> : !key ? <VaultUnlock header={header.data.header!} onRekeyed={() => void header.reload()} /> : <Vault header={header.data.header!} onHeaderChange={() => void header.reload()} />}
    </>
  )
}

function VaultSetup({ onDone }: { onDone: () => void }) {
  const toast = useToast()
  const { unlock } = useVault()
  const [pw, setPw] = useState('')
  const [pw2, setPw2] = useState('')
  const [busy, setBusy] = useState(false)
  const [recovery, setRecovery] = useState<{ code: string; key: CryptoKey } | null>(null)
  const [saved, setSaved] = useState(false)
  const s = passwordStrength(pw)
  const create = async (e: React.FormEvent) => {
    e.preventDefault()
    if (pw !== pw2) return toast.push('error', 'Master passwords do not match')
    if (pw.length < 10 || s.score < 3) return toast.push('error', 'Choose a stronger master password (at least 10 characters, “Good” or better).')
    setBusy(true)
    try { const v = await createVault(pw); await api.post('/api/vault/setup', v.request); setRecovery({ code: v.recoveryKey, key: v.key }) } catch (x) { toast.error(x) } finally { setBusy(false) }
  }
  if (recovery) return (
    <Card className="vault-locked"><Card.Body className="p-4">
      <FiKey size={36} className="text-brand-orange mb-2" /><h4 className="fw-bold">Save your recovery key</h4>
      <Alert variant="warning" className="small text-start">This is the <strong>only</strong> way to recover your vault if you forget the master password. NijMitra cannot reset it for you.</Alert>
      <div className="secret fs-5 p-3 border rounded mb-3 user-select-all">{recovery.code}</div>
      <Button variant="outline-primary" className="mb-3" onClick={() => { const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([`NijMitra vault recovery key\n${recovery.code}\n`], { type: 'text/plain' })); a.download = 'nijmitra-vault-recovery-key.txt'; a.click(); setSaved(true) }}><FiDownload /> Download</Button>
      <Form.Check id="rk" className="text-start mb-3" label="I have stored my recovery key safely" checked={saved} onChange={(e) => setSaved(e.target.checked)} />
      <Button className="w-100" disabled={!saved} onClick={() => { unlock(recovery.key); onDone() }}>Open my vault</Button>
    </Card.Body></Card>
  )
  return (
    <Card className="vault-locked"><Card.Body className="p-4">
      <FiShield size={40} className="text-primary mb-2" />
      <h4 className="fw-bold">Create your password vault</h4>
      <p className="text-muted small">Your master password encrypts everything on this device. It is never sent to NijMitra — not even we can read your passwords.</p>
      <Form onSubmit={create} className="text-start">
        <Form.Control type="password" className="mb-1" placeholder="Master password" autoComplete="new-password" value={pw} onChange={(e) => setPw(e.target.value)} />
        {pw && <div className="mb-2"><div className="strength"><div style={{ width: `${s.score * 25}%`, background: s.color }} /></div><small style={{ color: s.color }}>{s.label}</small></div>}
        <Form.Control type="password" className="mb-3" placeholder="Confirm master password" autoComplete="new-password" value={pw2} onChange={(e) => setPw2(e.target.value)} />
        <Button type="submit" className="w-100 btn-gradient" disabled={busy}>{busy ? <><Spinner size="sm" /> Securing…</> : 'Create vault'}</Button>
      </Form>
    </Card.Body></Card>
  )
}

function VaultUnlock({ header, onRekeyed }: { header: VaultHeader; onRekeyed: () => void }) {
  const { unlock } = useVault()
  const toast = useToast()
  const [pw, setPw] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState('')
  const [recover, setRecover] = useState(false)
  const [rk, setRk] = useState('')
  const [newPw, setNewPw] = useState('')
  const [newKey, setNewKey] = useState<string | null>(null)
  const reauth = useReauth()
  const go = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setErr('')
    const k = await unlockVault(pw, header)
    setBusy(false); setPw('')
    if (k) unlock(k); else setErr('Incorrect master password.')
  }
  const doRecover = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setErr('')
    try {
      if (passwordStrength(newPw).score < 3) { setErr('Choose a stronger new master password.'); return }
      const r = await rekeyVault(header, { recoveryKey: rk }, newPw)
      if (!r) { setErr('Recovery key is incorrect.'); return }
      const ok = await withReauth(reauth, () => api.put('/api/vault/key', r.request))
      if (ok === undefined) return
      setNewKey(r.recoveryKey); unlock(r.key); toast.success('Master password reset'); onRekeyed()
    } catch (x) { setErr(apiError(x).message) } finally { setBusy(false) }
  }
  if (newKey) return <Alert variant="success" className="vault-locked">New recovery key: <div className="secret fs-5 my-2">{newKey}</div>Save it — the old key no longer works.</Alert>
  return (
    <Card className="vault-locked"><Card.Body className="p-4">
      <FiLock size={40} className="text-primary mb-2" />
      <h4 className="fw-bold">{recover ? 'Recover your vault' : 'Vault locked'}</h4>
      {err && <Alert variant="danger" className="py-2 small">{err}</Alert>}
      {!recover ? (
        <Form onSubmit={go}>
          <Form.Control autoFocus type="password" className="mb-3" placeholder="Master password" autoComplete="current-password" value={pw} onChange={(e) => setPw(e.target.value)} />
          <Button type="submit" className="w-100" disabled={busy || !pw}>{busy ? <><Spinner size="sm" /> Unlocking…</> : <><FiUnlock /> Unlock</>}</Button>
          <Button variant="link" size="sm" className="mt-2" onClick={() => setRecover(true)}>Forgot master password?</Button>
        </Form>
      ) : (
        <Form onSubmit={doRecover} className="text-start">
          <Form.Control className="mb-2 secret" placeholder="Recovery key (XXXX-XXXX-…)" value={rk} onChange={(e) => setRk(e.target.value.toUpperCase())} />
          <Form.Control type="password" className="mb-3" placeholder="New master password" autoComplete="new-password" value={newPw} onChange={(e) => setNewPw(e.target.value)} />
          <Button type="submit" className="w-100" disabled={busy}>{busy ? <Spinner size="sm" /> : 'Reset master password'}</Button>
          <Button variant="link" size="sm" className="mt-2" onClick={() => setRecover(false)}>Back</Button>
        </Form>
      )}
    </Card.Body></Card>
  )
}

function Vault({ header, onHeaderChange }: { header: VaultHeader; onHeaderChange: () => void }) {
  const { key, lock, touch, lockAt } = useVault()
  const toast = useToast()
  const confirm = useConfirm()
  const reauth = useReauth()
  const [params, setParams] = useSearchParams()
  const [tab, setTab] = useState<'logins' | 'notes'>('logins')
  const [category, setCategory] = useState('')
  const [fav, setFav] = useState(false)
  const [q, setQ] = useState('')
  const search = useDebounced(q)
  const cats = useAsync(() => api.get<string[]>('/api/vault/categories').then((r) => r.data), [])
  const entries = useAsync(() => api.get<Entry[]>('/api/vault/entries', { params: { category: category || undefined, favorites: fav || undefined, search: search || undefined } }).then((r) => r.data), [category, fav, search])
  const [edit, setEdit] = useState<Partial<Entry> & { secret?: Secret } | null>(null)
  const [view, setView] = useState<Entry | null>(null)
  const [now, setNow] = useState(Date.now())
  const [rekey, setRekey] = useState(false)
  useEffect(() => { const t = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(t) }, [])
  useEffect(() => { touch() }, [tab, category, search, touch])
  useEffect(() => {
    const open = params.get('open')
    if (open && entries.data) { const e = entries.data.find((x) => x.id === open); if (e) setView(e); params.delete('open'); setParams(params, { replace: true }) }
    if (params.get('new')) { setEdit({ category: 'Other' }); params.delete('new'); setParams(params, { replace: true }) }
  }, [params, setParams, entries.data])
  if (!key) return null

  const exportBackup = async () => {
    try {
      const r = await withReauth(reauth, () => api.post('/api/vault/export'))
      if (!r) return
      const a = document.createElement('a')
      a.href = URL.createObjectURL(new Blob([JSON.stringify(r.data, null, 2)], { type: 'application/json' }))
      a.download = `nijmitra-vault-backup-${new Date().toISOString().slice(0, 10)}.json`; a.click()
      toast.success('Encrypted backup downloaded. It can only be opened with your master password.')
    } catch (x) { toast.error(x) }
  }

  const remaining = lockAt ? Math.max(0, Math.round((lockAt - now) / 1000)) : 0
  return (
    <>
      <Card className="mb-3"><Card.Body className="d-flex flex-wrap gap-2 align-items-center">
        <Nav variant="pills" activeKey={tab} onSelect={(k) => setTab(k as 'logins' | 'notes')}><Nav.Item><Nav.Link eventKey="logins"><FiKey /> Logins</Nav.Link></Nav.Item><Nav.Item><Nav.Link eventKey="notes"><FiFileText /> Secure notes</Nav.Link></Nav.Item></Nav>
        <span className="ms-auto small text-muted"><FiClock /> Auto-lock in {Math.floor(remaining / 60)}:{String(remaining % 60).padStart(2, '0')}</span>
        <Dropdown align="end"><Dropdown.Toggle size="sm" variant="outline-secondary">Vault</Dropdown.Toggle><Dropdown.Menu>
          <Dropdown.Item onClick={() => void exportBackup()}><FiDownload className="me-2" />Export encrypted backup</Dropdown.Item>
          <Dropdown.Item as="label" className="clickable"><FiUpload className="me-2" />Import backup / CSV<input type="file" hidden accept=".json,.csv" onChange={(e) => { const f = e.target.files?.[0]; if (f) void importFile(f, key, toast, () => void entries.reload()); e.target.value = '' }} /></Dropdown.Item>
          <Dropdown.Item onClick={() => setRekey(true)}><FiRefreshCw className="me-2" />Change master password</Dropdown.Item>
        </Dropdown.Menu></Dropdown>
        <Button size="sm" variant="outline-danger" onClick={lock}><FiLock /> Lock</Button>
      </Card.Body></Card>

      {tab === 'notes' ? <SecureNotes vaultKey={key} /> : (
        <div className="row g-3">
          <div className="col-lg-3"><Card><Card.Body className="p-2">
            <div className={`nm-nav-link ${!category && !fav ? 'active' : ''}`} role="button" onClick={() => { setCategory(''); setFav(false) }}>All logins</div>
            <div className={`nm-nav-link ${fav ? 'active' : ''}`} role="button" onClick={() => { setFav(!fav); setCategory('') }}><FiStar /> Favourites</div>
            <div className="nm-nav-label">Categories</div>
            {cats.data?.map((c) => <div key={c} className={`nm-nav-link py-1 ${category === c ? 'active' : ''}`} role="button" onClick={() => { setCategory(c); setFav(false) }}>{c}</div>)}
          </Card.Body></Card></div>
          <div className="col-lg-9">
            <div className="d-flex gap-2 mb-3"><Form.Control placeholder="Search by title, website, app or tag…" value={q} onChange={(e) => setQ(e.target.value)} /><Button onClick={() => setEdit({ category: category || 'Other' })}><FiPlus /> Add</Button></div>
            <Card>{entries.loading && !entries.data ? <Loading /> : entries.data?.length === 0 ? <EmptyState icon={<FiKey />} title="No saved logins" text="Add your Google, bank, social and work accounts." action={<Button onClick={() => setEdit({ category: 'Other' })}><FiPlus /> Add login</Button>} /> :
              <Card.Body>{entries.data?.map((e) => (
                <div key={e.id} className="list-row clickable" onClick={() => { setView(e); touch() }}>
                  <span className="avatar" style={{ width: 38, height: 38, background: 'var(--nm-gradient)' }}>{e.title[0]?.toUpperCase()}</span>
                  <div className="flex-grow-1 min-w-0"><div className="fw-medium truncate">{e.title} {e.isFavorite && <FiStar className="text-warning" />}</div><div className="small text-muted truncate">{e.websiteUrl ?? e.applicationName ?? ''} · {e.category}</div></div>
                  <small className="text-muted d-none d-md-block">{relative(e.updatedAt)}</small>
                </div>))}</Card.Body>}</Card>
          </div>
        </div>)}

      {view && <ViewEntry entry={view} vaultKey={key} onClose={() => setView(null)} onEdit={(secret) => { setEdit({ ...view, secret }); setView(null) }}
        onDelete={async () => { if (await confirm({ title: `Delete "${view.title}"?` })) { await api.delete(`/api/vault/entries/${view.id}`); setView(null); void entries.reload() } }}
        onFav={async () => { await api.post(`/api/vault/entries/${view.id}/favorite`, null, { params: { value: !view.isFavorite } }); setView({ ...view, isFavorite: !view.isFavorite }); void entries.reload() }} />}
      {edit && <EditEntry value={edit} vaultKey={key} categories={cats.data ?? []} onClose={(ok) => { setEdit(null); if (ok) { void entries.reload(); void cats.reload() } }} />}
      {rekey && <RekeyModal header={header} onClose={(ok) => { setRekey(false); if (ok) onHeaderChange() }} />}
    </>
  )
}

function ViewEntry({ entry, vaultKey, onClose, onEdit, onDelete, onFav }: { entry: Entry; vaultKey: CryptoKey; onClose: () => void; onEdit: (s: Secret) => void; onDelete: () => void; onFav: () => void }) {
  const toast = useToast()
  const [secret, setSecret] = useState<Secret | null>(null)
  const [show, setShow] = useState(false)
  const [history, setHistory] = useState<{ password: string; changedAt: string }[] | null>(null)
  useEffect(() => { decryptJson<Secret>(vaultKey, entry.encryptedData).then(setSecret).catch(() => toast.push('error', 'Could not decrypt this item.')) }, [entry, vaultKey, toast])
  useEffect(() => () => setSecret(null), [])
  const loadHistory = async () => {
    const r = await api.get<{ encryptedPassword: string; changedAt: string }[]>(`/api/vault/entries/${entry.id}/history`)
    setHistory(await Promise.all(r.data.map(async (h) => ({ password: await decryptText(vaultKey, h.encryptedPassword).catch(() => '(unreadable)'), changedAt: h.changedAt }))))
  }
  const field = (label: string, value?: string, secretField = false) => value ? (
    <div className="mb-3"><div className="small text-muted">{label}</div>
      <InputGroup><Form.Control readOnly className="secret" type={secretField && !show ? 'password' : 'text'} value={value} />
        {secretField && <Button variant="outline-secondary" onClick={() => setShow(!show)} aria-label="Show">{show ? <FiEyeOff /> : <FiEye />}</Button>}
        <Button variant="outline-secondary" onClick={() => void copySecret(value, toast, label)} aria-label={`Copy ${label}`}><FiCopy /></Button></InputGroup></div>) : null
  const url = entry.websiteUrl ? (entry.websiteUrl.includes('://') ? entry.websiteUrl : `https://${entry.websiteUrl}`) : null
  return (
    <Modal show onHide={onClose} centered>
      <Modal.Header closeButton><Modal.Title className="h5">{entry.title} <Badge bg="" className="badge-soft ms-1">{entry.category}</Badge></Modal.Title></Modal.Header>
      <Modal.Body>
        {url && <div className="mb-3 small"><a href={url} target="_blank" rel="noreferrer noopener">{entry.websiteUrl} <FiExternalLink /></a></div>}
        {!secret ? <Loading text="Decrypting…" /> : <>
          {field('Username', secret.username)}{field('Email', secret.email)}{field('Password', secret.password, true)}
          {secret.password && <div className="mb-3"><div className="strength"><div style={{ width: `${passwordStrength(secret.password).score * 25}%`, background: passwordStrength(secret.password).color }} /></div><small className="text-muted">Strength: {passwordStrength(secret.password).label}</small></div>}
          {secret.notes && <div className="mb-3"><div className="small text-muted">Notes</div><div className="border rounded p-2 small" style={{ whiteSpace: 'pre-wrap' }}>{secret.notes}</div></div>}
        </>}
        {entry.tags && <div>{entry.tags.split(',').map((t) => <span className="tag" key={t}>#{t.trim()}</span>)}</div>}
        <div className="small text-muted">Updated {dateTime(entry.updatedAt)}</div>
        {history === null ? <Button size="sm" variant="link" className="px-0" onClick={() => void loadHistory()}><FiClock /> Password history</Button> :
          <div className="mt-2"><div className="small fw-semibold">Previous passwords</div>{history.length === 0 ? <div className="small text-muted">None.</div> : history.map((h, i) => <div key={i} className="d-flex justify-content-between small border-bottom py-1"><span className="secret">{show ? h.password : '••••••••'}</span><span className="text-muted">{dateTime(h.changedAt)}</span></div>)}</div>}
      </Modal.Body>
      <Modal.Footer>
        <Button variant="link" className="text-danger me-auto" onClick={onDelete}><FiTrash2 /> Delete</Button>
        <Button variant="outline-secondary" onClick={onFav}><FiStar /> {entry.isFavorite ? 'Unfavourite' : 'Favourite'}</Button>
        <Button disabled={!secret} onClick={() => secret && onEdit(secret)}><FiEdit2 /> Edit</Button>
      </Modal.Footer>
    </Modal>
  )
}

function EditEntry({ value, vaultKey, categories, onClose }: { value: Partial<Entry> & { secret?: Secret }; vaultKey: CryptoKey; categories: string[]; onClose: (ok: boolean) => void }) {
  const toast = useToast()
  const [m, setM] = useState({ title: value.title ?? '', websiteUrl: value.websiteUrl ?? '', applicationName: value.applicationName ?? '', category: value.category ?? 'Other', tags: value.tags ?? '', isFavorite: value.isFavorite ?? false })
  const [s, setS] = useState<Secret>(value.secret ?? {})
  const [show, setShow] = useState(false)
  const [gen, setGen] = useState({ length: 18, symbols: true, digits: true })
  const [busy, setBusy] = useState(false)
  const strength = passwordStrength(s.password ?? '')
  const save = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true)
    try {
      const encryptedData = await encryptJson(vaultKey, s)
      const prev = value.id && value.secret?.password && value.secret.password !== s.password ? await encryptText(vaultKey, value.secret.password) : null
      const body = { ...m, encryptedData, previousEncryptedPassword: prev }
      if (value.id) await api.put(`/api/vault/entries/${value.id}`, body); else await api.post('/api/vault/entries', body)
      toast.success('Saved securely'); onClose(true)
    } catch (x) { toast.error(x) } finally { setBusy(false) }
  }
  return (
    <Modal show onHide={() => onClose(false)} centered size="lg"><Form onSubmit={save} autoComplete="off">
      <Modal.Header closeButton><Modal.Title className="h5">{value.id ? 'Edit login' : 'Add login'}</Modal.Title></Modal.Header>
      <Modal.Body className="row g-3">
        <Form.Group className="col-md-6"><Form.Label>Website / app name</Form.Label><Form.Control required value={m.title} onChange={(e) => setM({ ...m, title: e.target.value })} placeholder="e.g. Google" /></Form.Group>
        <Form.Group className="col-md-6"><Form.Label>Website URL</Form.Label><Form.Control value={m.websiteUrl} onChange={(e) => setM({ ...m, websiteUrl: e.target.value })} placeholder="https://google.com" /></Form.Group>
        <Form.Group className="col-md-6"><Form.Label>Application</Form.Label><Form.Control value={m.applicationName} onChange={(e) => setM({ ...m, applicationName: e.target.value })} placeholder="Mobile app (optional)" /></Form.Group>
        <Form.Group className="col-md-6"><Form.Label>Category</Form.Label><Form.Control list="vcats" value={m.category} onChange={(e) => setM({ ...m, category: e.target.value })} /><datalist id="vcats">{categories.map((c) => <option key={c} value={c} />)}</datalist></Form.Group>
        <div className="col-12"><Alert variant="light" className="border small py-2 mb-0"><FiLock /> The fields below are encrypted on this device before saving.</Alert></div>
        <Form.Group className="col-md-6"><Form.Label>Username</Form.Label><Form.Control value={s.username ?? ''} onChange={(e) => setS({ ...s, username: e.target.value })} autoComplete="off" /></Form.Group>
        <Form.Group className="col-md-6"><Form.Label>Email</Form.Label><Form.Control value={s.email ?? ''} onChange={(e) => setS({ ...s, email: e.target.value })} autoComplete="off" /></Form.Group>
        <Form.Group className="col-12"><Form.Label>Password</Form.Label>
          <InputGroup><Form.Control className="secret" type={show ? 'text' : 'password'} value={s.password ?? ''} onChange={(e) => setS({ ...s, password: e.target.value })} autoComplete="new-password" />
            <Button variant="outline-secondary" onClick={() => setShow(!show)}>{show ? <FiEyeOff /> : <FiEye />}</Button>
            <Button variant="outline-primary" onClick={() => { setS({ ...s, password: generatePassword(gen.length, { upper: true, lower: true, digits: gen.digits, symbols: gen.symbols }) }); setShow(true) }}><FiRefreshCw /> Generate</Button></InputGroup>
          {s.password && <div className="mt-1"><div className="strength"><div style={{ width: `${strength.score * 25}%`, background: strength.color }} /></div><small style={{ color: strength.color }}>{strength.label}</small></div>}
          <div className="d-flex flex-wrap gap-3 align-items-center mt-2 small"><span>Length {gen.length}</span><Form.Range min={8} max={40} value={gen.length} onChange={(e) => setGen({ ...gen, length: Number(e.target.value) })} style={{ maxWidth: 180 }} />
            <Form.Check inline id="gsym" label="Symbols" checked={gen.symbols} onChange={(e) => setGen({ ...gen, symbols: e.target.checked })} /><Form.Check inline id="gdig" label="Numbers" checked={gen.digits} onChange={(e) => setGen({ ...gen, digits: e.target.checked })} /></div>
        </Form.Group>
        <Form.Group className="col-12"><Form.Label>Notes</Form.Label><Form.Control as="textarea" rows={2} value={s.notes ?? ''} onChange={(e) => setS({ ...s, notes: e.target.value })} /></Form.Group>
        <Form.Group className="col-md-8"><Form.Label>Tags</Form.Label><Form.Control value={m.tags} onChange={(e) => setM({ ...m, tags: e.target.value })} placeholder="personal, work" /></Form.Group>
        <Form.Check className="col-md-4 align-self-end" type="switch" id="favf" label="Favourite" checked={m.isFavorite} onChange={(e) => setM({ ...m, isFavorite: e.target.checked })} />
      </Modal.Body>
      <Modal.Footer><Button variant="outline-secondary" onClick={() => onClose(false)}>Cancel</Button><Button type="submit" disabled={busy}>{busy ? <Spinner size="sm" /> : 'Save'}</Button></Modal.Footer>
    </Form></Modal>
  )
}

function SecureNotes({ vaultKey }: { vaultKey: CryptoKey }) {
  const toast = useToast()
  const confirm = useConfirm()
  const notes = useAsync(async () => {
    const r = await api.get<Note[]>('/api/vault/notes')
    return Promise.all(r.data.map(async (n) => ({ ...n, plain: await decryptJson<{ title: string; content: string }>(vaultKey, n.encryptedData).catch(() => ({ title: '(unreadable)', content: '' })) })))
  }, [vaultKey])
  const [edit, setEdit] = useState<{ id?: string; title: string; content: string; isFavorite: boolean } | null>(null)
  const save = async () => {
    if (!edit) return
    try {
      const body = { encryptedData: await encryptJson(vaultKey, { title: edit.title, content: edit.content }), isFavorite: edit.isFavorite }
      if (edit.id) await api.put(`/api/vault/notes/${edit.id}`, body); else await api.post('/api/vault/notes', body)
      setEdit(null); void notes.reload(); toast.success('Note saved securely')
    } catch (x) { toast.error(x) }
  }
  return (
    <>
      <div className="text-end mb-3"><Button onClick={() => setEdit({ title: '', content: '', isFavorite: false })}><FiPlus /> New secure note</Button></div>
      {notes.loading ? <Loading /> : notes.data?.length === 0 ? <Card><EmptyState icon={<FiFileText />} title="No secure notes" text="Store Wi-Fi passwords, PINs, licence keys or private notes — encrypted." /></Card> :
        <div className="row g-3">{notes.data?.map((n) => (
          <div className="col-md-6 col-xl-4" key={n.id}><Card className="h-100 clickable" onClick={() => setEdit({ id: n.id, title: n.plain.title, content: n.plain.content, isFavorite: n.isFavorite })}><Card.Body>
            <div className="d-flex justify-content-between"><h6 className="fw-bold">{n.plain.title} {n.isFavorite && <FiStar className="text-warning" />}</h6>
              <Button size="sm" variant="link" className="text-danger p-0" onClick={async (e) => { e.stopPropagation(); if (await confirm({ title: 'Delete this note?' })) { await api.delete(`/api/vault/notes/${n.id}`); void notes.reload() } }}><FiTrash2 /></Button></div>
            <div className="small text-muted" style={{ filter: 'blur(4px)' }}>{n.plain.content.slice(0, 80) || '…'}</div>
            <div className="small text-muted mt-2">{relative(n.updatedAt)}</div>
          </Card.Body></Card></div>))}</div>}
      <Modal show={!!edit} onHide={() => setEdit(null)} centered size="lg">
        <Modal.Header closeButton><Modal.Title className="h5">Secure note</Modal.Title></Modal.Header>
        <Modal.Body>
          <Form.Control className="mb-2" placeholder="Title" value={edit?.title ?? ''} onChange={(e) => setEdit(edit && { ...edit, title: e.target.value })} />
          <Form.Control as="textarea" rows={8} placeholder="Your private note…" value={edit?.content ?? ''} onChange={(e) => setEdit(edit && { ...edit, content: e.target.value })} />
          <Form.Check className="mt-2" type="switch" id="nfav" label="Favourite" checked={edit?.isFavorite ?? false} onChange={(e) => setEdit(edit && { ...edit, isFavorite: e.target.checked })} />
        </Modal.Body>
        <Modal.Footer><Button variant="outline-secondary" onClick={() => setEdit(null)}>Cancel</Button><Button disabled={!edit?.title} onClick={() => void save()}>Save</Button></Modal.Footer>
      </Modal>
    </>
  )
}

function RekeyModal({ header, onClose }: { header: VaultHeader; onClose: (ok: boolean) => void }) {
  const toast = useToast()
  const reauth = useReauth()
  const { unlock } = useVault()
  const [cur, setCur] = useState('')
  const [next, setNext] = useState('')
  const [rk, setRk] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const go = async () => {
    setBusy(true)
    try {
      if (passwordStrength(next).score < 3) { toast.push('error', 'Choose a stronger new master password.'); return }
      const r = await rekeyVault(header, { master: cur }, next)
      if (!r) { toast.push('error', 'Current master password is incorrect.'); return }
      const ok = await withReauth(reauth, () => api.put('/api/vault/key', r.request))
      if (ok === undefined) return
      unlock(r.key); setRk(r.recoveryKey)
    } catch (x) { toast.error(x) } finally { setBusy(false) }
  }
  const strength = useMemo(() => passwordStrength(next), [next])
  return (
    <Modal show onHide={() => onClose(!!rk)} centered>
      <Modal.Header closeButton><Modal.Title className="h5">Change master password</Modal.Title></Modal.Header>
      <Modal.Body>
        {rk ? <Alert variant="success">Master password changed. Your new recovery key:<div className="secret fs-5 my-2 user-select-all">{rk}</div>Save it — the previous recovery key no longer works.</Alert> : <>
          <Form.Control type="password" className="mb-2" placeholder="Current master password" value={cur} onChange={(e) => setCur(e.target.value)} />
          <Form.Control type="password" className="mb-1" placeholder="New master password" value={next} onChange={(e) => setNext(e.target.value)} />
          {next && <div className="strength mb-1"><div style={{ width: `${strength.score * 25}%`, background: strength.color }} /></div>}
          <div className="small text-muted">Your saved items stay as they are; only the key that protects them is re-wrapped.</div></>}
      </Modal.Body>
      <Modal.Footer>{rk ? <Button onClick={() => onClose(true)}>Done</Button> : <Button disabled={busy || !cur || !next} onClick={() => void go()}>{busy ? <Spinner size="sm" /> : 'Change'}</Button>}</Modal.Footer>
    </Modal>
  )
}

/** Imports a NijMitra encrypted backup (asks for that backup's master password) or a CSV export (Chrome, Edge, Bitwarden…). */
async function importFile(file: File, vaultKey: CryptoKey, toast: ReturnType<typeof useToast>, done: () => void) {
  try {
    const text = await file.text()
    const entries: { title: string; websiteUrl?: string; category: string; tags?: string; isFavorite: boolean; encryptedData: string }[] = []
    const notes: { encryptedData: string; isFavorite: boolean }[] = []
    if (file.name.toLowerCase().endsWith('.json')) {
      const backup = JSON.parse(text) as { header: VaultHeader; entries: Entry[]; notes: Note[] }
      const pw = prompt('Enter the master password that was used for this backup')
      if (!pw) return
      const bkKey = await unlockVault(pw, backup.header)
      if (!bkKey) { toast.push('error', 'Wrong master password for this backup.'); return }
      for (const e of backup.entries) entries.push({ title: e.title, websiteUrl: e.websiteUrl, category: e.category, tags: e.tags, isFavorite: e.isFavorite, encryptedData: await encryptJson(vaultKey, await decryptJson(bkKey, e.encryptedData)) })
      for (const n of backup.notes) notes.push({ isFavorite: n.isFavorite, encryptedData: await encryptJson(vaultKey, await decryptJson(bkKey, n.encryptedData)) })
    } else {
      const rows = parseCsv(text)
      const head = rows.shift()?.map((h) => h.trim().toLowerCase()) ?? []
      const col = (...names: string[]) => head.findIndex((h) => names.includes(h))
      const [iName, iUrl, iUser, iPass, iNote] = [col('name', 'title'), col('url', 'login_uri', 'website'), col('username', 'login_username', 'user'), col('password', 'login_password'), col('note', 'notes')]
      if (iPass < 0) { toast.push('error', 'CSV must contain a "password" column.'); return }
      for (const r of rows) {
        if (!r[iPass]) continue
        const url = iUrl >= 0 ? r[iUrl] : undefined
        entries.push({ title: (iName >= 0 && r[iName]) || (url ? new URL(url.includes('://') ? url : `https://${url}`).hostname : 'Imported'), websiteUrl: url, category: 'Other', isFavorite: false,
          encryptedData: await encryptJson(vaultKey, { username: iUser >= 0 ? r[iUser] : undefined, password: r[iPass], notes: iNote >= 0 ? r[iNote] : undefined }) })
      }
    }
    const res = await api.post('/api/vault/import', { entries, notes })
    toast.success(`Imported ${res.data.imported} item(s). Delete the import file from your device now.`)
    done()
  } catch (x) { toast.error(x, 'Import failed — the file is not a supported backup.') }
}

function parseCsv(text: string): string[][] {
  const rows: string[][] = []; let row: string[] = []; let f = ''; let q = false
  for (let i = 0; i < text.length; i++) {
    const c = text[i]!
    if (q) { if (c === '"' && text[i + 1] === '"') { f += '"'; i++ } else if (c === '"') q = false; else f += c; continue }
    if (c === '"') q = true; else if (c === ',') { row.push(f); f = '' } else if (c === '\n') { row.push(f); rows.push(row); row = []; f = '' } else if (c !== '\r') f += c
  }
  if (f || row.length) { row.push(f); rows.push(row) }
  return rows
}
