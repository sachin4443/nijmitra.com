import { useEffect, useState } from 'react'
import { Button, Toast, ToastContainer } from 'react-bootstrap'
import { FiDownloadCloud, FiRefreshCw } from 'react-icons/fi'
import { api } from '../lib/api'

interface BeforeInstallPromptEvent extends Event { prompt: () => Promise<void>; userChoice: Promise<{ outcome: string }> }

let deferred: BeforeInstallPromptEvent | null = null
window.addEventListener('beforeinstallprompt', (e) => { e.preventDefault(); deferred = e as BeforeInstallPromptEvent; window.dispatchEvent(new Event('nm-installable')) })

/** PWA: install prompt (Android/desktop), iOS "Add to Home Screen" hint, and update-available prompt. */
export function PwaPrompts() {
  const [installable, setInstallable] = useState(!!deferred)
  const [needRefresh, setNeedRefresh] = useState(false)
  const [updateSw, setUpdateSw] = useState<(() => void) | null>(null)
  const [dismissed, setDismissed] = useState(() => { try { return localStorage.getItem('nm-install-dismissed') === '1' } catch { return false } })
  const isIos = /iphone|ipad|ipod/i.test(navigator.userAgent) && !('standalone' in navigator && (navigator as { standalone?: boolean }).standalone)
  const standalone = window.matchMedia('(display-mode: standalone)').matches

  useEffect(() => {
    const h = () => setInstallable(true)
    window.addEventListener('nm-installable', h)
    if (import.meta.env.PROD && 'serviceWorker' in navigator) {
      import('workbox-window').then(({ Workbox }) => {
        const wb = new Workbox('/sw.js')
        wb.addEventListener('waiting', () => { setNeedRefresh(true); setUpdateSw(() => () => { wb.addEventListener('controlling', () => window.location.reload()); wb.messageSW({ type: 'SKIP_WAITING' }) }) })
        void wb.register()
      }).catch(() => {})
    }
    return () => window.removeEventListener('nm-installable', h)
  }, [])

  const dismiss = () => { setDismissed(true); try { localStorage.setItem('nm-install-dismissed', '1') } catch { /* ignore */ } }
  const showInstall = !standalone && !dismissed && (installable || isIos)

  return (
    <ToastContainer position="bottom-start" className="p-3" style={{ zIndex: 1990, position: 'fixed' }}>
      {needRefresh && (
        <Toast onClose={() => setNeedRefresh(false)}>
          <Toast.Header><FiRefreshCw className="me-2 text-primary" /><strong className="me-auto">Update available</strong></Toast.Header>
          <Toast.Body>A new version of NijMitra is ready. <Button size="sm" className="ms-2" onClick={() => updateSw?.()}>Update now</Button></Toast.Body>
        </Toast>
      )}
      {showInstall && (
        <Toast onClose={dismiss}>
          <Toast.Header><FiDownloadCloud className="me-2 text-primary" /><strong className="me-auto">Install NijMitra</strong></Toast.Header>
          <Toast.Body>
            {installable ? <>Install the app for quick access and notifications. <Button size="sm" className="ms-2" onClick={async () => { await deferred?.prompt(); deferred = null; setInstallable(false); dismiss() }}>Install</Button></>
              : <>On iPhone/iPad: tap <strong>Share</strong> → <strong>Add to Home Screen</strong>.</>}
          </Toast.Body>
        </Toast>
      )}
    </ToastContainer>
  )
}

const urlB64ToUint8Array = (b64: string) => {
  const padding = '='.repeat((4 - (b64.length % 4)) % 4)
  const raw = atob((b64 + padding).replace(/-/g, '+').replace(/_/g, '/'))
  return Uint8Array.from(raw, (c) => c.charCodeAt(0))
}

/** Subscribes this browser to Web Push using the admin-configured VAPID public key. */
export async function enablePush(): Promise<string> {
  if (!('serviceWorker' in navigator) || !('PushManager' in window)) return 'Push notifications are not supported on this browser.'
  const info = await api.get('/api/system/info')
  const key = info.data.vapidPublicKey as string | null
  if (!key) return 'Push notifications have not been configured by the administrator yet.'
  const perm = await Notification.requestPermission()
  if (perm !== 'granted') return 'Notification permission was not granted.'
  const reg = await navigator.serviceWorker.ready
  const sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: urlB64ToUint8Array(key) as BufferSource })
  const json = sub.toJSON() as { endpoint: string; keys: { p256dh: string; auth: string } }
  await api.post('/api/me/push-subscriptions', { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth })
  return 'ok'
}
