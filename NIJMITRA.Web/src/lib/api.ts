import axios, { AxiosError, type AxiosRequestConfig } from 'axios'

/**
 * HTTP client. The access token lives only in memory (never localStorage). The refresh token is an HttpOnly,
 * SameSite=Strict cookie scoped to /api/auth, sent together with the anti-CSRF header below.
 */
export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? ''

let accessToken: string | null = null
let sessionEndedHandler: ((reason: string) => void) | null = null
let refreshing: Promise<string | null> | null = null

export const getAccessToken = () => accessToken
export const setAccessToken = (t: string | null) => { accessToken = t }
export const onSessionEnded = (h: (reason: string) => void) => { sessionEndedHandler = h }

export function deviceKey(): string {
  try {
    let k = localStorage.getItem('nm-device')
    if (!k) { k = crypto.randomUUID(); localStorage.setItem('nm-device', k) }
    return k
  } catch { return 'unknown-device' }
}

export const api = axios.create({ baseURL: API_BASE, withCredentials: true, headers: { 'X-Requested-With': 'NIJMITRA' } })

api.interceptors.request.use((cfg) => {
  if (accessToken) cfg.headers.Authorization = `Bearer ${accessToken}`
  cfg.headers['X-Device-Key'] = deviceKey()
  return cfg
})

const SESSION_CODES = ['session_expired', 'session_revoked', 'account_inactive']

export async function refreshAccessToken(): Promise<string | null> {
  if (!refreshing) {
    refreshing = axios.post(`${API_BASE}/api/auth/refresh`, null, { withCredentials: true, headers: { 'X-Requested-With': 'NIJMITRA' } })
      .then((r) => { accessToken = r.data.accessToken; return accessToken })
      .catch(() => { accessToken = null; return null })
      .finally(() => { setTimeout(() => { refreshing = null }, 0) })
  }
  return refreshing
}

api.interceptors.response.use(undefined, async (error: AxiosError<{ code?: string }>) => {
  const cfg = error.config as (AxiosRequestConfig & { _retried?: boolean }) | undefined
  const status = error.response?.status
  const code = error.response?.data?.code
  if (status === 401 && cfg && !cfg._retried && !cfg.url?.includes('/api/auth/')) {
    if (code && SESSION_CODES.includes(code)) { sessionEndedHandler?.(code); return Promise.reject(error) }
    cfg._retried = true
    const token = await refreshAccessToken()
    if (token) return api(cfg)
    sessionEndedHandler?.('session_expired')
  }
  return Promise.reject(error)
})

export interface ApiError { message: string; code?: string; fields: Record<string, string> }

/** Normalises server validation/problem responses into a friendly message and per-field errors. */
export function apiError(err: unknown): ApiError {
  const e = err as AxiosError<{ title?: string; code?: string; errors?: Record<string, string[]> }>
  const data = e?.response?.data
  const fields: Record<string, string> = {}
  if (data?.errors) for (const [k, v] of Object.entries(data.errors)) fields[k.charAt(0).toLowerCase() + k.slice(1)] = Array.isArray(v) ? v[0] : String(v)
  let message = data?.title ?? (e?.response ? 'Request failed.' : 'Cannot reach the server. Check your connection.')
  if (data?.code === 'validation_failed' && Object.keys(fields).length > 0 && Object.keys(fields).length <= 3) message = Object.values(fields).join(' ')
  if (e?.response?.status === 413) message = 'The file is too large.'
  return { message, code: data?.code, fields }
}

/** Downloads a protected file through the authenticated client and saves it with the server-supplied name. */
export async function downloadFile(url: string, fallbackName = 'download') {
  const res = await api.get(url, { responseType: 'blob' })
  const cd = res.headers['content-disposition'] as string | undefined
  const match = cd && /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd)
  const name = match ? decodeURIComponent(match[1]) : fallbackName
  const href = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = href; a.download = name; document.body.appendChild(a); a.click(); a.remove()
  setTimeout(() => URL.revokeObjectURL(href), 2000)
}

/** Loads a protected file as an object URL for previews (PDF, images, audio, video). Caller must revoke it. */
export async function blobUrl(url: string): Promise<string> {
  const res = await api.get(url, { responseType: 'blob' })
  return URL.createObjectURL(res.data as Blob)
}

export const passive = { headers: { 'X-Activity': 'passive' } }

export async function uploadFile<T = { id: string; name: string; contentType: string; sizeBytes: number }>(url: string, file: File, fields?: Record<string, string>, onProgress?: (p: number) => void): Promise<T> {
  const form = new FormData()
  form.append('file', file)
  for (const [k, v] of Object.entries(fields ?? {})) if (v !== undefined && v !== null) form.append(k, v)
  const res = await api.post<T>(url, form, { onUploadProgress: (e) => onProgress?.(e.total ? Math.round((e.loaded / e.total) * 100) : 0) })
  return res.data
}
