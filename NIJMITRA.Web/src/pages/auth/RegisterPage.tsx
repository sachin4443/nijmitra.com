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
import { useI18n, type TKey } from '../../lib/i18n'

const makeSchema = (t: (k: TKey) => string) => z.object({
  fullName: z.string().trim().min(2, t('reg.e.name')).max(150),
  mobile: z.string().trim().regex(/^(\+?91[\s-]?)?[6-9]\d{4}[\s-]?\d{5}$/, t('reg.e.mobile')),
  mobileOtp: z.string().optional(),
  password: z.string().min(8, t('reg.e.pwLen')).max(128)
    .refine((p) => [/[A-Z]/, /[a-z]/, /\d/, /[^A-Za-z0-9]/].filter((r) => r.test(p)).length >= 3, t('reg.e.pwMix')),
  confirmPassword: z.string(),
  pin: z.string().regex(/^\d{4,6}$/, t('reg.e.pin')),
  acceptTerms: z.literal(true, { message: t('reg.e.terms') }),
}).refine((v) => v.password === v.confirmPassword, { path: ['confirmPassword'], message: t('reg.e.match') })
type Values = z.infer<ReturnType<typeof makeSchema>>

export default function RegisterPage() {
  const nav = useNavigate()
  const { t } = useI18n()
  const [config, setConfig] = useState<{ allowRegistration: boolean; requireMobileOtp: boolean } | null>(null)
  const [error, setError] = useState('')
  const [otpSent, setOtpSent] = useState(false)
  const [busy, setBusy] = useState(false)
  const { register, handleSubmit, watch, getValues, setError: setFieldError, formState: { errors } } = useForm<Values>({ resolver: zodResolver(makeSchema(t)) })
  useEffect(() => { api.get('/api/auth/registration-config').then((r) => setConfig(r.data)).catch(() => setConfig({ allowRegistration: true, requireMobileOtp: false })) }, [])
  const strength = passwordStrength(watch('password') ?? '')

  const sendOtp = async () => {
    setError('')
    try { await api.post('/api/auth/register/send-otp', { mobile: getValues('mobile') }); setOtpSent(true) } catch (e) { setError(apiError(e).message) }
  }

  const submit = async (v: Values) => {
    if (config?.requireMobileOtp && !v.mobileOtp) { setFieldError('mobileOtp', { message: t('reg.e.otp') }); return }
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

  if (config && !config.allowRegistration) return <AuthShell><Alert variant="info">{t('reg.closed')}</Alert><Link to="/login">{t('reg.back')}</Link></AuthShell>

  return (
    <AuthShell>
      <h2 className="fw-bold mb-1">{t('reg.title')}</h2>
      <p className="text-muted mb-4">{t('reg.sub')}</p>
      {error && <Alert variant="danger" className="py-2 small">{error}</Alert>}
      <Form onSubmit={handleSubmit(submit)} noValidate>
        <Form.Group className="mb-3"><Form.Label>{t('reg.name')}</Form.Label>
          <Form.Control autoComplete="name" {...register('fullName')} isInvalid={!!errors.fullName} /><Form.Control.Feedback type="invalid">{errors.fullName?.message}</Form.Control.Feedback></Form.Group>
        <Form.Group className="mb-3"><Form.Label>{t('reg.mobile')}</Form.Label>
          <InputGroup><InputGroup.Text>+91</InputGroup.Text><Form.Control type="tel" inputMode="tel" autoComplete="tel" {...register('mobile')} isInvalid={!!errors.mobile} />
            {config?.requireMobileOtp && <Button variant="outline-primary" onClick={() => void sendOtp()}>{otpSent ? t('reg.resend') : t('reg.sendOtp')}</Button>}
            <Form.Control.Feedback type="invalid">{errors.mobile?.message}</Form.Control.Feedback></InputGroup>
        </Form.Group>
        {config?.requireMobileOtp && (
          <Form.Group className="mb-3"><Form.Label>{t('reg.otp')}</Form.Label>
            <Form.Control inputMode="numeric" maxLength={6} autoComplete="one-time-code" {...register('mobileOtp')} isInvalid={!!errors.mobileOtp} placeholder={otpSent ? t('reg.otpPh') : t('reg.otpPhFirst')} />
            <Form.Control.Feedback type="invalid">{errors.mobileOtp?.message}</Form.Control.Feedback></Form.Group>
        )}
        <div className="row g-3">
          <Form.Group className="col-sm-6"><Form.Label>{t('reg.password')}</Form.Label>
            <Form.Control type="password" autoComplete="new-password" {...register('password')} isInvalid={!!errors.password} /><Form.Control.Feedback type="invalid">{errors.password?.message}</Form.Control.Feedback>
            {strength.label && <div className="mt-1"><div className="strength"><div style={{ width: `${strength.score * 25}%`, background: strength.color }} /></div><small style={{ color: strength.color }}>{strength.label}</small></div>}
          </Form.Group>
          <Form.Group className="col-sm-6"><Form.Label>{t('reg.confirm')}</Form.Label>
            <Form.Control type="password" autoComplete="new-password" {...register('confirmPassword')} isInvalid={!!errors.confirmPassword} /><Form.Control.Feedback type="invalid">{errors.confirmPassword?.message}</Form.Control.Feedback></Form.Group>
        </div>
        <Form.Group className="my-3"><Form.Label>{t('reg.pin')} <span className="text-muted small">{t('reg.pinHelp')}</span></Form.Label>
          <Form.Control type="password" inputMode="numeric" maxLength={6} autoComplete="off" {...register('pin')} isInvalid={!!errors.pin} /><Form.Control.Feedback type="invalid">{errors.pin?.message}</Form.Control.Feedback></Form.Group>
        <Form.Check className="mb-3" id="terms" isInvalid={!!errors.acceptTerms} feedback={errors.acceptTerms?.message} feedbackType="invalid"
          label={<span className="small">{t('reg.agree1')} <a href="/terms.html" target="_blank" rel="noreferrer">{t('reg.terms')}</a> {t('reg.and')} <a href="/privacy.html" target="_blank" rel="noreferrer">{t('reg.privacy')}</a>.</span>} {...register('acceptTerms')} />
        <Button type="submit" className="w-100 btn-gradient py-2" disabled={busy}>{busy ? <Spinner size="sm" /> : <><FiCheckCircle className="me-2" />{t('reg.submit')}</>}</Button>
      </Form>
      <p className="text-center mt-4 mb-0 small">{t('reg.have')} <Link to="/login" className="fw-semibold">{t('reg.signin')}</Link></p>
    </AuthShell>
  )
}
