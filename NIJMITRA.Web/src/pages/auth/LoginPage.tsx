import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { Alert, Button, Form, InputGroup, Spinner } from 'react-bootstrap'
import { useForm } from 'react-hook-form'
import { FiEye, FiEyeOff, FiKey, FiShield, FiSmartphone } from 'react-icons/fi'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { api, apiError, deviceKey } from '../../lib/api'
import { useAuth } from '../../contexts/AuthContext'
import AuthShell from './AuthShell'
import { RecoveryCodes, TotpSetup } from '../settings/TwoFactor'

const schema = z.object({
  mobile: z.string().trim().min(10, 'Enter your 10-digit mobile number').max(16),
  password: z.string().min(1, 'Enter your password'),
})
type Values = z.infer<typeof schema>

const REASONS: Record<string, string> = {
  idle: 'You were signed out after a period of inactivity.',
  expired: 'Your session has ended. Please sign in again.',
  inactive: 'Your account is not active. Please contact support.',
}

export default function LoginPage() {
  const { completeLogin, signedOutReason } = useAuth()
  const nav = useNavigate()
  const loc = useLocation()
  const [step, setStep] = useState<'password' | 'otp' | 'setup' | 'codes'>('password')
  const [challenge, setChallenge] = useState('')
  const [code, setCode] = useState('')
  const [useRecovery, setUseRecovery] = useState(false)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [show, setShow] = useState(false)
  const [notice, setNotice] = useState('')
  const [codes, setCodes] = useState<string[]>([])
  const [pendingToken, setPendingToken] = useState('')
  const { register, handleSubmit, formState: { errors } } = useForm<Values>({ resolver: zodResolver(schema), defaultValues: { mobile: (loc.state as { mobile?: string })?.mobile ?? '' } })
  const after = (loc.state as { from?: string })?.from ?? '/'

  const finish = async (token: string) => { await completeLogin(token); nav(after, { replace: true }) }

  const onPassword = async (v: Values) => {
    setBusy(true); setError('')
    try {
      const r = await api.post('/api/auth/login', { ...v, deviceKey: deviceKey() })
      if (r.data.status === 'success') await finish(r.data.accessToken)
      else if (r.data.status === 'two_factor_required') { setChallenge(r.data.challengeToken); setStep('otp') }
      else if (r.data.status === 'two_factor_setup_required') { setChallenge(r.data.challengeToken); setNotice(r.data.message); setStep('setup') }
    } catch (e) { setError(apiError(e).message) } finally { setBusy(false) }
  }

  const onOtp = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setError('')
    try {
      const r = await api.post('/api/auth/login/2fa', { challengeToken: challenge, code, isRecoveryCode: useRecovery })
      await finish(r.data.accessToken)
    } catch (err) {
      const x = apiError(err)
      setError(x.message)
      if (x.code === 'challenge_expired') { setStep('password'); setCode('') }
    } finally { setBusy(false) }
  }

  return (
    <AuthShell>
      {step === 'password' && (
        <>
          <h2 className="fw-bold mb-1">Welcome back</h2>
          <p className="text-muted mb-4">Sign in with your mobile number and password.</p>
          {signedOutReason && REASONS[signedOutReason] && <Alert variant="warning" className="py-2 small">{REASONS[signedOutReason]}</Alert>}
          {error && <Alert variant="danger" className="py-2 small">{error}</Alert>}
          <Form onSubmit={handleSubmit(onPassword)} noValidate>
            <Form.Group className="mb-3">
              <Form.Label>Mobile number</Form.Label>
              <InputGroup><InputGroup.Text><FiSmartphone /></InputGroup.Text>
                <Form.Control type="tel" inputMode="tel" autoComplete="username" placeholder="98765 43210" {...register('mobile')} isInvalid={!!errors.mobile} />
                <Form.Control.Feedback type="invalid">{errors.mobile?.message}</Form.Control.Feedback></InputGroup>
            </Form.Group>
            <Form.Group className="mb-2">
              <Form.Label>Password</Form.Label>
              <InputGroup><InputGroup.Text><FiKey /></InputGroup.Text>
                <Form.Control type={show ? 'text' : 'password'} autoComplete="current-password" {...register('password')} isInvalid={!!errors.password} />
                <Button variant="outline-secondary" onClick={() => setShow(!show)} aria-label="Show password">{show ? <FiEyeOff /> : <FiEye />}</Button>
                <Form.Control.Feedback type="invalid">{errors.password?.message}</Form.Control.Feedback></InputGroup>
            </Form.Group>
            <div className="text-end mb-3"><Link to="/forgot-password" className="small">Forgot password?</Link></div>
            <Button type="submit" className="w-100 btn-gradient py-2" disabled={busy}>{busy ? <Spinner size="sm" /> : 'Sign in'}</Button>
          </Form>
          <p className="text-center mt-4 mb-0 small">New to NijMitra? <Link to="/register" className="fw-semibold">Create your account</Link></p>
        </>
      )}

      {step === 'otp' && (
        <>
          <div className="text-center mb-3"><span className="avatar" style={{ width: 56, height: 56 }}><FiShield size={26} /></span></div>
          <h2 className="fw-bold mb-1 text-center">Two-step verification</h2>
          <p className="text-muted text-center mb-4">{useRecovery ? 'Enter one of your saved recovery codes.' : 'Open Google Authenticator or Microsoft Authenticator and enter the 6-digit code for NijMitra.'}</p>
          {error && <Alert variant="danger" className="py-2 small">{error}</Alert>}
          <Form onSubmit={onOtp}>
            <Form.Control autoFocus className="text-center fs-4 secret mb-3" style={{ letterSpacing: useRecovery ? '.1em' : '.5em' }} value={code}
              onChange={(e) => setCode(useRecovery ? e.target.value.toUpperCase() : e.target.value.replace(/\D/g, '').slice(0, 6))}
              inputMode={useRecovery ? 'text' : 'numeric'} autoComplete="one-time-code" placeholder={useRecovery ? 'XXXXX-XXXXX' : '000000'} />
            <Button type="submit" className="w-100 btn-gradient py-2" disabled={busy || (!useRecovery && code.length !== 6)}>{busy ? <Spinner size="sm" /> : 'Verify & sign in'}</Button>
          </Form>
          <div className="d-flex justify-content-between mt-3 small">
            <a href="#" onClick={(e) => { e.preventDefault(); setUseRecovery(!useRecovery); setCode(''); setError('') }}>{useRecovery ? 'Use authenticator code' : 'Use a recovery code'}</a>
            <a href="#" onClick={(e) => { e.preventDefault(); setStep('password'); setCode('') }}>Back</a>
          </div>
        </>
      )}

      {step === 'setup' && (
        <>
          <h2 className="fw-bold mb-1">Set up two-step verification</h2>
          {notice && <Alert variant="info" className="py-2 small">{notice}</Alert>}
          <TotpSetup
            begin={() => api.post('/api/auth/login/2fa/setup', { challengeToken: challenge }).then((r) => r.data)}
            confirm={async (c) => {
              const r = await api.post('/api/auth/login/2fa/enable', { challengeToken: challenge, code: c })
              setCodes(r.data.recoveryCodes ?? []); setPendingToken(r.data.accessToken); setStep('codes')
            }} />
        </>
      )}

      {step === 'codes' && <RecoveryCodes codes={codes} onDone={() => void finish(pendingToken)} />}
    </AuthShell>
  )
}
