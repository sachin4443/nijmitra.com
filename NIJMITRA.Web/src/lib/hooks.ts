import { useCallback, useEffect, useRef, useState } from 'react'

/** Loads data and exposes {data, loading, error, reload}. Ignores out-of-order responses. */
export function useAsync<T>(fn: () => Promise<T>, deps: unknown[]) {
  const [data, setData] = useState<T | undefined>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>()
  const seq = useRef(0)
  const run = useCallback(async () => {
    const id = ++seq.current
    setLoading(true)
    try {
      const d = await fn()
      if (id === seq.current) { setData(d); setError(undefined) }
    } catch (e) { if (id === seq.current) setError(e) }
    finally { if (id === seq.current) setLoading(false) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)
  useEffect(() => { void run() }, [run])
  return { data, setData, loading, error, reload: run }
}

export function useDebounced<T>(value: T, ms = 300) {
  const [v, setV] = useState(value)
  useEffect(() => { const t = setTimeout(() => setV(value), ms); return () => clearTimeout(t) }, [value, ms])
  return v
}

export function useMediaQuery(q: string) {
  const [m, setM] = useState(() => window.matchMedia(q).matches)
  useEffect(() => {
    const mq = window.matchMedia(q)
    const h = () => setM(mq.matches)
    mq.addEventListener('change', h)
    return () => mq.removeEventListener('change', h)
  }, [q])
  return m
}
