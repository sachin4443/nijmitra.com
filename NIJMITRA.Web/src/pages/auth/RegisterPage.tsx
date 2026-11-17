import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useState } from 'react'
import { Alert, Button, Form, InputGroup, Spinner } from 'react-bootstrap'
import { useForm } from 'react-hook-form'
import { FiCheckCircle } from 'react-icons/fi'
import { Link, useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { api, apiError } from '../../lib/api'
import { passwordStrength } from '../../lib/vaultCrypto'
import AuthShell from './AuthShell'

const schema = z.object({
  fullName: z.string().trim().min(2, 'Enter your full name').max(150),
  mobile: z.string().trim().regex(/^(\+?91[\s-]?)?[6-9]\d{4}[\s-]?\d{5}$/, 'Enter a valid 10-digit Indian mobile number'),
  mobileOtp: z.string().optional(),
  password: z.string().min(8, 'At least 8 characters').max(128)
    .refine((p) => [/[A-Z]/, /[a-z]/, /\d/, /[^A-Za-z0-9]/].filter((r) => r.test(p)).length >= 3, 'Use 3 of: uppercase, lowercase, number, symbol'),
  confirmPassword: z.string(),
  pin: z.string().regex(/^\d{4,6}$/, 'PIN must be 4–6 digits'),
  acceptTerms: z.literal(true, { message: 'Please accept the terms to continue' }),
}).refine((v) => v.password === v.confirmPassword, { path: ['confirmPassword'], message: 'Passwords do not match' })
type Values = z.infer<typeof schema>

export default function RegisterPage() {
  const nav = useNavigate()
  const [config, setConfig] = useState<{ allowRegistration: boolean; requireMobileOtp: boolean } | null>(null)
  const [error, setError] = useState('')
  const [otpSent, setOtpSent] = useState(false)
  const [busy, setBusy] = useState(false)
  const { register, handleSubmit, watch, getValues, setError: setFieldError, formState: { errors } } = useForm<Values>({ resolver: zodResolver(schema) })
  useEffect(() => { api.get('/api/auth/registration-config').then((r) => setConfig(r.data)).catch(() => setConfig({ allowRegistration: true, requireMobileOtp: false })) }, [])
  const strength = passwordStrength(watch('password') ?? '')

  const sendOtp = async () => {
    setError('')
    try { await api.post('/api/auth/register/send-otp', { mobile: getValues('mobile') }); setOtpSent(true) } catch (e) { setError(apiError(e).message) }
  }

  const submit = async (v: Values) => {
    if (config?.requireMobileOtp && !v.mobileOtp) { setFieldError('mobileOtp', { message: 'Enter the code sent to your mobile' }); return }
    setBusy(true); setError('')
    try {
      await api.post('/api/auth/register', v)
      nav('/login', { state: { mobile: v.mobile, registered: true } })
    } catch (e) {
      const x = apiError(e)
      setError(x.message)
      for (const [k, m] of Object.entries(x.fields)) setFieldError(k as keyof Values, { message: m })
    } finally { setBusy(false) }
  }

  if (config && !config.allowRegistration) return <AuthShell><Alert variant="info">New registrations are currently closed. Please check back later.</Alert><Link to="/login">Back to sign in</Link></AuthShell>

  return (
    <AuthShell>
      <h2 className="fw-bold mb-1">Create your account</h2>
      <p className="text-muted mb-4">It takes less than a minute. You can enable two-step verification after signing in.</p>
      {error && <Alert variant="danger" className="py-2 small">{error}</Alert>}
      <Form onSubmit={handleSubmit(submit)} noValidate>
        <Form.Group className="mb-3"><Form.Label>Full name</Form.Label>
          <Form.Control autoComplete="name" {...register('fullName')} isInvalid={!!errors.fullName} /><Form.Control.Feedback type="invalid">{errors.fullName?.message}</Form.Control.Feedback></Form.Group>
        <Form.Group className="mb-3"><Form.Label>Mobile number</Form.Label>
          <InputGroup><InputGroup.Text>+91</InputGroup.Text><Form.Control type="tel" inputMode="tel" autoComplete="tel" {...register('mobile')} isInvalid={!!errors.mobile} />
            {config?.requireMobileOtp && <Button variant="outline-primary" onClick={() => void sendOtp()}>{otpSent ? 'Resend' : 'Send OTP'}</Button>}
            <Form.Control.Feedback type="invalid">{errors.mobile?.message}</Form.Control.Feedback></InputGroup>
        </Form.Group>
        {config?.requireMobileOtp && (
          <Form.Group className="mb-3"><Form.Label>Mobile OTP</Form.Label>
            <Form.Control inputMode="numeric" maxLength={6} autoComplete="one-time-code" {...register('mobileOtp')} isInvalid={!!errors.mobileOtp} placeholder={otpSent ? 'Enter the 6-digit code' : 'Tap "Send OTP" first'} />
            <Form.Control.Feedback type="invalid">{errors.mobileOtp?.message}</Form.Control.Feedback></Form.Group>
        )}
        <div className="row g-3">
          <Form.Group className="col-sm-6"><Form.Label>Password</Form.Label>
            <Form.Control type="password" autoComplete="new-password" {...register('password')} isInvalid={!!errors.password} /><Form.Control.Feedback type="invalid">{errors.password?.message}</Form.Control.Feedback>
            {strength.label && <div className="mt-1"><div className="strength"><div style={{ width: `${strength.score * 25}%`, background: strength.color }} /></div><small style={{ color: strength.color }}>{strength.label}</small></div>}
          </Form.Group>
          <Form.Group className="col-sm-6"><Form.Label>Confirm password</Form.Label>
            <Form.Control type="password" autoComplete="new-password" {...register('confirmPassword')} isInvalid={!!errors.confirmPassword} /><Form.Control.Feedback type="invalid">{errors.confirmPassword?.message}</Form.Control.Feedback></Form.Group>
        </div>
        <Form.Group className="my-3"><Form.Label>Personal security PIN <span className="text-muted small">(4–6 digits, used to confirm sensitive actions)</span></Form.Label>
          <Form.Control type="password" inputMode="numeric" maxLength={6} autoComplete="off" {...register('pin')} isInvalid={!!errors.pin} /><Form.Control.Feedback type="invalid">{errors.pin?.message}</Form.Control.Feedback></Form.Group>
        <Form.Check className="mb-3" id="terms" isInvalid={!!errors.acceptTerms} feedback={errors.acceptTerms?.message} feedbackType="invalid"
          label={<span className="small">I agree to the <a href="/terms.html" target="_blank" rel="noreferrer">Terms of Use</a> and <a href="/privacy.html" target="_blank" rel="noreferrer">Privacy Policy</a>.</span>} {...register('acceptTerms')} />
        <Button type="submit" className="w-100 btn-gradient py-2" disabled={busy}>{busy ? <Spinner size="sm" /> : <><FiCheckCircle className="me-2" />Create account</>}</Button>
      </Form>
      <p className="text-center mt-4 mb-0 small">Already have an account? <Link to="/login" className="fw-semibold">Sign in</Link></p>
    </AuthShell>
  )
}
