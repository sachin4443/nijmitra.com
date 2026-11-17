import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'

export type Theme = 'system' | 'light' | 'dark'
interface Ctx { theme: Theme; resolved: 'light' | 'dark'; setTheme: (t: Theme) => void }
const ThemeCtx = createContext<Ctx>({ theme: 'system', resolved: 'light', setTheme: () => {} })

const read = (): Theme => { try { return (localStorage.getItem('nm-theme') as Theme) || 'system' } catch { return 'system' } }

export function ThemeProvider({ children, onChange }: { children: ReactNode; onChange?: (t: Theme) => void }) {
  const [theme, setThemeState] = useState<Theme>(read)
  const [systemDark, setSystemDark] = useState(() => window.matchMedia('(prefers-color-scheme: dark)').matches)
  useEffect(() => {
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    const h = () => setSystemDark(mq.matches)
    mq.addEventListener('change', h)
    return () => mq.removeEventListener('change', h)
  }, [])
  const resolved = theme === 'system' ? (systemDark ? 'dark' : 'light') : theme
  useEffect(() => { document.documentElement.setAttribute('data-bs-theme', resolved) }, [resolved])
  const setTheme = useCallback((t: Theme) => {
    setThemeState(t)
    try { localStorage.setItem('nm-theme', t) } catch { /* ignore */ }
    onChange?.(t)
  }, [onChange])
  return <ThemeCtx.Provider value={{ theme, resolved, setTheme }}>{children}</ThemeCtx.Provider>
}

export const useTheme = () => useContext(ThemeCtx)
