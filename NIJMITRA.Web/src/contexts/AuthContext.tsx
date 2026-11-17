import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { api, onSessionEnded, refreshAccessToken, setAccessToken } from '../lib/api'

export interface Me {
  id: string; publicId: string; fullName: string; mobileNumber: string; email: string | null; emailVerified: boolean; pendingEmail: string | null
  twoFactorEnabled: boolean; hasPin: boolean; mustChangePassword: boolean; roles: string[]; permissions: string[]
  isImpersonated: boolean; impersonatorName: string | null
  profile: { avatarFileId: string | null; address: string | null; dateOfBirth: string | null; city: string | null; state: string | null; country: string | null; timeZone: string; language: string; theme: string }
  sessionPolicy: { idleTimeoutSeconds: number; warningSeconds: number }
}

interface Ctx {
  status: 'loading' | 'anonymous' | 'authenticated'
  me: Me | null
  signedOutReason: string | null
  completeLogin: (accessToken: string) => Promise<void>
  reloadMe: () => Promise<void>
  logout: (reason?: string) => Promise<void>
  can: (permission: string) => boolean
  isAdmin: boolean
}

const AuthCtx = createContext<Ctx>(null as unknown as Ctx)
const listeners = new Set<() => void>()
/** Lets other modules (vault, realtime) wipe sensitive state on logout. */
export const onLogout = (fn: () => void) => { listeners.add(fn); return () => { listeners.delete(fn) } }

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<Ctx['status']>('loading')
  const [me, setMe] = useState<Me | null>(null)
  const [reason, setReason] = useState<string | null>(null)
  const loggingOut = useRef(false)

  const reloadMe = useCallback(async () => { const r = await api.get<Me>('/api/me'); setMe(r.data) }, [])

  const clearClient = useCallback((why: string | null) => {
    setAccessToken(null)
    setMe(null)
    setStatus('anonymous')
    setReason(why)
    listeners.forEach((l) => { try { l() } catch { /* ignore */ } })
    try { sessionStorage.clear() } catch { /* ignore */ }
    navigator.serviceWorker?.controller?.postMessage({ type: 'LOGOUT' })
  }, [])

  const logout = useCallback(async (why?: string) => {
    if (loggingOut.current) return
    loggingOut.current = true
    try { await api.post('/api/auth/logout' + (why === 'idle' ? '?reason=idle' : '')) } catch { /* session may already be gone */ }
    clearClient(why ?? null)
    try { new BroadcastChannel('nm-session').postMessage({ type: 'logout', reason: why }) } catch { /* ignore */ }
    loggingOut.current = false
  }, [clearClient])

  const completeLogin = useCallback(async (token: string) => {
    setAccessToken(token)
    await reloadMe()
    setReason(null)
    setStatus('authenticated')
  }, [reloadMe])

  useEffect(() => {
    onSessionEnded((code) => { if (!loggingOut.current) clearClient(code === 'account_inactive' ? 'inactive' : 'expired') })
    let ch: BroadcastChannel | undefined
    try {
      ch = new BroadcastChannel('nm-session')
      ch.onmessage = (e) => { if (e.data?.type === 'logout') clearClient(e.data.reason ?? null) }
    } catch { /* unsupported */ }
    // Restore the session from the HttpOnly refresh cookie (fails if idle-expired).
    void (async () => {
      const token = await refreshAccessToken()
      if (!token) { setStatus('anonymous'); return }
      try { await reloadMe(); setStatus('authenticated') } catch { clearClient(null) }
    })()
    return () => ch?.close()
  }, [clearClient, reloadMe])

  const value = useMemo<Ctx>(() => ({
    status, me, signedOutReason: reason, completeLogin, reloadMe, logout,
    can: (p) => !!me?.permissions.includes(p), isAdmin: !!me && me.permissions.length > 0
  }), [status, me, reason, completeLogin, reloadMe, logout])
  return <AuthCtx.Provider value={value}>{children}</AuthCtx.Provider>
}

export const useAuth = () => useContext(AuthCtx)
