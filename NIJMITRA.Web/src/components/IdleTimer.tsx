import { useEffect, useRef, useState } from 'react'
import { Button, Modal } from 'react-bootstrap'
import { FiClock } from 'react-icons/fi'
import { api } from '../lib/api'
import { useAuth } from '../contexts/AuthContext'

const EVENTS = ['mousemove', 'mousedown', 'keydown', 'touchstart', 'pointerdown', 'wheel', 'scroll'] as const

/**
 * Automatic logout after inactivity (admin-configurable, default 60 s). Genuine input events reset the timer,
 * activity is shared across tabs (BroadcastChannel) and reported to the server (≤ every 5 s) so the server-side
 * idle check stays in sync. A warning with a countdown appears before the session ends.
 */
export default function IdleTimer() {
  const { me, logout } = useAuth()
  const timeout = (me?.sessionPolicy.idleTimeoutSeconds ?? 60) * 1000
  const warning = (me?.sessionPolicy.warningSeconds ?? 15) * 1000
  const last = useRef(Date.now())
  const lastPing = useRef(0)
  const [remaining, setRemaining] = useState<number | null>(null)
  const channel = useRef<BroadcastChannel | null>(null)

  useEffect(() => {
    try { channel.current = new BroadcastChannel('nm-activity') } catch { channel.current = null }
    const ping = () => {
      const now = Date.now()
      if (now - lastPing.current > 5000) { lastPing.current = now; api.post('/api/auth/activity').catch(() => {}) }
    }
    let throttle = 0
    const onActivity = () => {
      const now = Date.now()
      if (now - throttle < 500) return
      throttle = now
      last.current = now
      setRemaining(null)
      channel.current?.postMessage(now)
      ping()
    }
    if (channel.current) channel.current.onmessage = (e) => { last.current = Math.max(last.current, Number(e.data) || 0); setRemaining(null) }
    EVENTS.forEach((e) => window.addEventListener(e, onActivity, { passive: true }))
    const vis = () => { if (document.visibilityState === 'visible') tick() }
    document.addEventListener('visibilitychange', vis)
    const tick = () => {
      const idle = Date.now() - last.current
      if (idle >= timeout) { void logout('idle'); return }
      setRemaining(idle >= timeout - warning ? Math.ceil((timeout - idle) / 1000) : null)
    }
    const t = setInterval(tick, 1000)
    return () => {
      clearInterval(t)
      EVENTS.forEach((e) => window.removeEventListener(e, onActivity))
      document.removeEventListener('visibilitychange', vis)
      channel.current?.close()
    }
  }, [timeout, warning, logout])

  const stay = () => { last.current = Date.now(); lastPing.current = 0; setRemaining(null); api.post('/api/auth/activity').catch(() => {}) }

  return (
    <Modal show={remaining !== null} centered backdrop="static" keyboard={false} size="sm">
      <Modal.Body className="text-center p-4">
        <FiClock size={36} className="text-warning mb-2" />
        <h5 className="fw-bold">Still there?</h5>
        <p className="text-muted mb-1">For your security you will be signed out automatically in</p>
        <div className="display-6 fw-bold text-primary mb-3">{remaining}s</div>
        <div className="d-grid gap-2">
          <Button onClick={stay}>Stay signed in</Button>
          <Button variant="outline-secondary" onClick={() => void logout()}>Sign out now</Button>
        </div>
      </Modal.Body>
    </Modal>
  )
}
