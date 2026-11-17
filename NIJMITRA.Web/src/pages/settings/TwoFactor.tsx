import { useEffect, useState } from 'react'
import { Alert, Button, Form, Spinner } from 'react-bootstrap'
import { FiCopy, FiDownload, FiShield } from 'react-icons/fi'
import { apiError } from '../../lib/api'

interface Setup { secret: string; qrCodeDataUri: string; otpAuthUri: string }

/** Authenticator enrolment: QR code + manual key + 6-digit confirmation. */
export function TotpSetup({ begin, confirm }: { begin: () => Promise<Setup>; confirm: (code: string) => Promise<void> }) {
  const [setup, setSetup] = useState<Setup | null>(null)
  const [code, setCode] = useState('')
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  useEffect(() => { begin().then(setSetup).catch((e) => setErr(apiError(e).message)) }, [begin])
  const submit = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setErr('')
    try { await confirm(code) } catch (x) { setErr(apiError(x).message) } finally { setBusy(false) }
  }
  if (!setup) return err ? <Alert variant="danger">{err}</Alert> : <div className="text-center py-4"><Spinner /></div>
  return (
    <div>
      <ol className="small ps-3">
        <li>Install <strong>Google Authenticator</strong> or <strong>Microsoft Authenticator</strong> on your phone.</li>
        <li>Tap “+” and scan this QR code (or enter the key manually).</li>
        <li>Type the 6-digit code shown in the app.</li>
      </ol>
      <div className="text-center my-3"><img src={setup.qrCodeDataUri} alt="Authenticator QR code" width={200} height={200} className="bg-white p-2 rounded-3 border" /></div>
      <div className="small text-muted text-center mb-1">Manual setup key</div>
      <div className="d-flex justify-content-center align-items-center gap-2 mb-3">
        <code className="secret fs-6">{setup.secret}</code>
        <Button size="sm" variant="link" onClick={() => void navigator.clipboard.writeText(setup.secret.replace(/\s/g, ''))} title="Copy"><FiCopy /></Button>
      </div>
      {err && <Alert variant="danger" className="py-2 small">{err}</Alert>}
      <Form onSubmit={submit} className="d-flex gap-2">
        <Form.Control className="text-center secret fs-5" inputMode="numeric" autoComplete="one-time-code" placeholder="000000" value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))} />
        <Button type="submit" disabled={busy || code.length !== 6}>{busy ? <Spinner size="sm" /> : 'Verify'}</Button>
      </Form>
    </div>
  )
}

export function RecoveryCodes({ codes, onDone }: { codes: string[]; onDone: () => void }) {
  const [saved, setSaved] = useState(false)
  const text = `NijMitra recovery codes (each works once)\nGenerated ${new Date().toLocaleString()}\n\n${codes.join('\n')}\n`
  const download = () => {
    const a = document.createElement('a')
    a.href = URL.createObjectURL(new Blob([text], { type: 'text/plain' }))
    a.download = 'nijmitra-recovery-codes.txt'; a.click(); setSaved(true)
  }
  return (
    <div>
      <div className="text-center mb-3"><FiShield size={36} className="text-success" /><h4 className="fw-bold mt-2">Save your recovery codes</h4></div>
      <Alert variant="warning" className="small">If you lose your phone, these one-time codes are the only way to sign in. Store them somewhere safe and private.</Alert>
      <div className="row g-2 mb-3">{codes.map((c) => <div className="col-6" key={c}><div className="secret text-center border rounded py-1">{c}</div></div>)}</div>
      <div className="d-flex gap-2 mb-3">
        <Button variant="outline-primary" className="flex-fill" onClick={download}><FiDownload /> Download</Button>
        <Button variant="outline-primary" className="flex-fill" onClick={() => { void navigator.clipboard.writeText(codes.join('\n')); setSaved(true) }}><FiCopy /> Copy</Button>
      </div>
      <Form.Check id="saved" label="I have saved my recovery codes" checked={saved} onChange={(e) => setSaved(e.target.checked)} className="mb-3" />
      <Button className="w-100" disabled={!saved} onClick={onDone}>Continue</Button>
    </div>
  )
}
