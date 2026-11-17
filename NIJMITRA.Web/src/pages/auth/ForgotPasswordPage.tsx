import { useState } from 'react'
import { Alert, Button, Form, Spinner } from 'react-bootstrap'
import { Link, useNavigate } from 'react-router-dom'
import { api, apiError } from '../../lib/api'
import AuthShell from './AuthShell'

export default function ForgotPasswordPage() {
  const nav = useNavigate()
  const [mobile, setMobile] = useState('')
  const [sent, setSent] = useState(false)
  const [otp, setOtp] = useState('')
  const [pw, setPw] = useState('')
  const [pw2, setPw2] = useState('')
  const [msg, setMsg] = useState('')
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)

  const request = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setErr('')
    try { const r = await api.post('/api/auth/password-reset/request', { mobile }); setMsg(r.data.message); setSent(true) }
    catch (x) { setErr(apiError(x).message) } finally { setBusy(false) }
  }
  const confirm = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setErr('')
    try { await api.post('/api/auth/password-reset/confirm', { mobile, otp, newPassword: pw, confirmPassword: pw2 }); nav('/login', { state: { mobile } }) }
    catch (x) { setErr(apiError(x).message) } finally { setBusy(false) }
  }

  return (
    <AuthShell>
      <h2 className="fw-bold mb-1">Reset password</h2>
      <p className="text-muted mb-4">We'll send a verification code to your registered mobile number or email.</p>
      {err && <Alert variant="danger" className="py-2 small">{err}</Alert>}
      {msg && <Alert variant="success" className="py-2 small">{msg}</Alert>}
      {!sent ? (
        <Form onSubmit={request}>
          <Form.Group className="mb-3"><Form.Label>Mobile number</Form.Label><Form.Control type="tel" value={mobile} onChange={(e) => setMobile(e.target.value)} required /></Form.Group>
          <Button type="submit" className="w-100 btn-gradient" disabled={busy || mobile.length < 10}>{busy ? <Spinner size="sm" /> : 'Send code'}</Button>
        </Form>
      ) : (
        <Form onSubmit={confirm}>
          <Form.Group className="mb-3"><Form.Label>Verification code</Form.Label><Form.Control inputMode="numeric" maxLength={6} value={otp} onChange={(e) => setOtp(e.target.value)} required /></Form.Group>
          <Form.Group className="mb-3"><Form.Label>New password</Form.Label><Form.Control type="password" autoComplete="new-password" value={pw} onChange={(e) => setPw(e.target.value)} required /></Form.Group>
          <Form.Group className="mb-3"><Form.Label>Confirm new password</Form.Label><Form.Control type="password" autoComplete="new-password" value={pw2} onChange={(e) => setPw2(e.target.value)} required /></Form.Group>
          <Button type="submit" className="w-100 btn-gradient" disabled={busy}>{busy ? <Spinner size="sm" /> : 'Set new password'}</Button>
        </Form>
      )}
      <p className="text-center mt-4 mb-0 small"><Link to="/login">Back to sign in</Link></p>
    </AuthShell>
  )
}
