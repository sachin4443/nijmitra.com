import { createContext, useCallback, useContext, useState, type ReactNode } from 'react'
import { Toast, ToastContainer } from 'react-bootstrap'
import { FiAlertTriangle, FiBell, FiCheckCircle, FiInfo } from 'react-icons/fi'
import { apiError } from '../lib/api'

type Kind = 'success' | 'error' | 'info' | 'notify'
interface Item { id: number; kind: Kind; title: string; body?: string; onClick?: () => void }
interface Ctx { push: (kind: Kind, title: string, body?: string, onClick?: () => void) => void; error: (e: unknown, fallback?: string) => void; success: (t: string) => void }
const ToastCtx = createContext<Ctx>({ push: () => {}, error: () => {}, success: () => {} })

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<Item[]>([])
  const remove = (id: number) => setItems((x) => x.filter((i) => i.id !== id))
  const push = useCallback((kind: Kind, title: string, body?: string, onClick?: () => void) => {
    const id = Date.now() + Math.random()
    setItems((x) => [...x.slice(-4), { id, kind, title, body, onClick }])
  }, [])
  const error = useCallback((e: unknown, fallback?: string) => push('error', fallback && !(e as { response?: unknown })?.response ? fallback : apiError(e).message), [push])
  const success = useCallback((t: string) => push('success', t), [push])
  const icon = { success: <FiCheckCircle className="text-success" />, error: <FiAlertTriangle className="text-danger" />, info: <FiInfo className="text-primary" />, notify: <FiBell className="text-warning" /> }
  return (
    <ToastCtx.Provider value={{ push, error, success }}>
      {children}
      <ToastContainer position="bottom-end" className="p-3" style={{ zIndex: 2000, position: 'fixed' }}>
        {items.map((t) => (
          <Toast key={t.id} onClose={() => remove(t.id)} delay={t.kind === 'error' ? 6000 : 4000} autohide onClick={() => { t.onClick?.(); if (t.onClick) remove(t.id) }} className={t.onClick ? 'clickable' : ''}>
            <Toast.Header closeButton><span className="me-2 d-inline-flex">{icon[t.kind]}</span><strong className="me-auto">{t.title}</strong></Toast.Header>
            {t.body && <Toast.Body>{t.body}</Toast.Body>}
          </Toast>
        ))}
      </ToastContainer>
    </ToastCtx.Provider>
  )
}

export const useToast = () => useContext(ToastCtx)
