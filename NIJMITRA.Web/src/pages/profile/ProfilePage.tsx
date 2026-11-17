import { useEffect, useState } from 'react'
import { Badge, Button, Card, Form, Spinner } from 'react-bootstrap'
import { FiCamera, FiCopy, FiShield } from 'react-icons/fi'
import { api, uploadFile } from '../../lib/api'
import { useAuth } from '../../contexts/AuthContext'
import { useToast } from '../../contexts/ToastContext'
import { Avatar, PageHeader, LinkButton } from '../../components/ui'

const ZONES = ['Asia/Kolkata', 'Asia/Dubai', 'Asia/Singapore', 'Europe/London', 'America/New_York', 'America/Los_Angeles', 'Australia/Sydney', 'UTC']

export default function ProfilePage() {
  const { me, reloadMe } = useAuth()
  const toast = useToast()
  const [f, setF] = useState({ fullName: '', address: '', dateOfBirth: '', city: '', state: '', country: '', timeZone: 'Asia/Kolkata', language: 'en' })
  const [busy, setBusy] = useState(false)
  const [avatarBusy, setAvatarBusy] = useState(false)
  useEffect(() => {
    if (!me) return
    const p = me.profile
    setF({ fullName: me.fullName, address: p.address ?? '', dateOfBirth: p.dateOfBirth ?? '', city: p.city ?? '', state: p.state ?? '', country: p.country ?? '', timeZone: p.timeZone, language: p.language })
  }, [me])
  if (!me) return null
  const save = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true)
    try { await api.put('/api/me/profile', { ...f, dateOfBirth: f.dateOfBirth || null }); await reloadMe(); toast.success('Profile saved') } catch (x) { toast.error(x) } finally { setBusy(false) }
  }
  const avatar = async (file?: File) => {
    if (!file) return
    setAvatarBusy(true)
    try { await uploadFile('/api/me/avatar', file); await reloadMe(); toast.success('Photo updated') } catch (x) { toast.error(x) } finally { setAvatarBusy(false) }
  }
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) => setF({ ...f, [k]: e.target.value })
  return (
    <>
      <PageHeader title="My Profile" subtitle="Your personal details" />
      <div className="row g-3">
        <div className="col-lg-4">
          <Card className="text-center"><Card.Body className="p-4">
            <div className="position-relative d-inline-block mb-3">
              <Avatar name={me.fullName} src={me.profile.avatarFileId ? `/api/me/avatar/${me.id}` : null} size={112} />
              <label className="btn btn-sm btn-primary rounded-circle position-absolute bottom-0 end-0" title="Change photo">
                {avatarBusy ? <Spinner size="sm" /> : <FiCamera />}<input type="file" hidden accept="image/png,image/jpeg,image/webp" onChange={(e) => void avatar(e.target.files?.[0])} />
              </label>
            </div>
            <h5 className="fw-bold mb-0">{me.fullName}</h5>
            <div className="text-muted mb-2">{me.mobileNumber}</div>
            <div className="d-inline-flex align-items-center gap-2 badge-soft rounded-pill px-3 py-1 fw-semibold">
              NijMitra ID: {me.publicId}<Button size="sm" variant="link" className="p-0" onClick={() => { void navigator.clipboard.writeText(me.publicId); toast.success('ID copied') }}><FiCopy /></Button>
            </div>
            <p className="small text-muted mt-2">Share your ID so friends can send you a request.</p>
            <hr />
            <div className="text-start small">
              <div className="d-flex justify-content-between mb-1"><span>Two-step verification</span>{me.twoFactorEnabled ? <Badge bg="" className="bg-soft-success">On</Badge> : <Badge bg="" className="bg-soft-warning">Off</Badge>}</div>
              <div className="d-flex justify-content-between mb-1"><span>Email</span><span>{me.email ?? '—'} {me.email && (me.emailVerified ? '✓' : '(unverified)')}</span></div>
              <div className="d-flex justify-content-between"><span>Security PIN</span><span>{me.hasPin ? 'Set' : 'Not set'}</span></div>
            </div>
            <LinkButton to="/settings/security" variant="outline-primary" size="sm" className="mt-3"><FiShield /> Security settings</LinkButton>
          </Card.Body></Card>
        </div>
        <div className="col-lg-8">
          <Card><Card.Body>
            <Form onSubmit={save}>
              <div className="row g-3">
                <Form.Group className="col-md-6"><Form.Label>Full name</Form.Label><Form.Control value={f.fullName} onChange={set('fullName')} required minLength={2} /></Form.Group>
                <Form.Group className="col-md-6"><Form.Label>Mobile</Form.Label><Form.Control value={me.mobileNumber} disabled /></Form.Group>
                <Form.Group className="col-md-6"><Form.Label>Date of birth</Form.Label><Form.Control type="date" value={f.dateOfBirth} onChange={set('dateOfBirth')} /></Form.Group>
                <Form.Group className="col-md-6"><Form.Label>Email</Form.Label><Form.Control value={me.email ?? ''} disabled placeholder="Add from Settings → Security" /></Form.Group>
                <Form.Group className="col-12"><Form.Label>Address</Form.Label><Form.Control as="textarea" rows={2} value={f.address} onChange={set('address')} /></Form.Group>
                <Form.Group className="col-md-4"><Form.Label>City</Form.Label><Form.Control value={f.city} onChange={set('city')} /></Form.Group>
                <Form.Group className="col-md-4"><Form.Label>State</Form.Label><Form.Control value={f.state} onChange={set('state')} /></Form.Group>
                <Form.Group className="col-md-4"><Form.Label>Country</Form.Label><Form.Control value={f.country} onChange={set('country')} /></Form.Group>
                <Form.Group className="col-md-6"><Form.Label>Time zone</Form.Label><Form.Select value={f.timeZone} onChange={set('timeZone')}>{ZONES.map((z) => <option key={z}>{z}</option>)}</Form.Select></Form.Group>
                <Form.Group className="col-md-6"><Form.Label>Language</Form.Label><Form.Select value={f.language} onChange={set('language')}><option value="en">English</option><option value="hi">हिंदी</option></Form.Select></Form.Group>
              </div>
              <Button type="submit" className="mt-3" disabled={busy}>{busy ? <Spinner size="sm" /> : 'Save profile'}</Button>
            </Form>
          </Card.Body></Card>
        </div>
      </div>
    </>
  )
}
