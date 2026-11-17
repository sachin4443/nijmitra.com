import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import { onLogout } from './AuthContext'

interface Ctx { key: CryptoKey | null; unlock: (k: CryptoKey) => void; lock: () => void; touch: () => void; lockAt: number | null }
const VaultCtx = createContext<Ctx>({ key: null, unlock: () => {}, lock: () => {}, touch: () => {}, lockAt: null })
const AUTO_LOCK_MS = 5 * 60 * 1000

/** Holds the decrypted (non-extractable) vault key in memory only; auto-locks after 5 minutes without vault use. */
export function VaultProvider({ children }: { children: ReactNode }) {
  const [key, setKey] = useState<CryptoKey | null>(null)
  const [lockAt, setLockAt] = useState<number | null>(null)
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const lock = useCallback(() => { setKey(null); setLockAt(null); if (timer.current) clearTimeout(timer.current) }, [])
  const touch = useCallback(() => {
    if (timer.current) clearTimeout(timer.current)
    setLockAt(Date.now() + AUTO_LOCK_MS)
    timer.current = setTimeout(lock, AUTO_LOCK_MS)
  }, [lock])
  const unlock = useCallback((k: CryptoKey) => { setKey(k); touch() }, [touch])
  useEffect(() => onLogout(lock), [lock])
  useEffect(() => {
    const hide = () => { if (document.visibilityState === 'hidden' && timer.current) { clearTimeout(timer.current); timer.current = setTimeout(lock, 60_000) } }
    document.addEventListener('visibilitychange', hide)
    return () => document.removeEventListener('visibilitychange', hide)
  }, [lock])
  return <VaultCtx.Provider value={{ key, unlock, lock, touch, lockAt }}>{children}</VaultCtx.Provider>
}

export const useVault = () => useContext(VaultCtx)
