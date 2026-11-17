import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import { API_BASE, getAccessToken, refreshAccessToken } from '../lib/api'
import { useAuth } from './AuthContext'

type Handler = (payload: never) => void
interface Ctx { connected: boolean; on: (event: string, h: Handler) => () => void; invoke: (method: string, ...args: unknown[]) => Promise<void> }
const RtCtx = createContext<Ctx>({ connected: false, on: () => () => {}, invoke: async () => {} })
const EVENTS = ['notification', 'message', 'messageDeleted', 'messageExpired', 'typing', 'read', 'delivered', 'presence', 'friendRequest', 'friendAccepted', 'conversationUpdated', 'sessionEnded']

/** One SignalR connection per signed-in user: notifications, chat, typing, presence. Heartbeat every 25 s. */
export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { status, logout } = useAuth()
  const [connected, setConnected] = useState(false)
  const conn = useRef<HubConnection | null>(null)
  const handlers = useRef(new Map<string, Set<Handler>>())

  useEffect(() => {
    if (status !== 'authenticated') return
    const c = new HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/app`, { accessTokenFactory: async () => getAccessToken() ?? (await refreshAccessToken()) ?? '' })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
      .configureLogging(LogLevel.Warning)
      .build()
    conn.current = c
    EVENTS.forEach((evt) => c.on(evt, (payload) => handlers.current.get(evt)?.forEach((h) => (h as (p: unknown) => void)(payload))))
    c.on('sessionEnded', () => void logout('idle'))
    c.onreconnected(() => setConnected(true))
    c.onreconnecting(() => setConnected(false))
    c.onclose(() => setConnected(false))
    let stopped = false
    const start = async () => {
      try { await c.start(); if (!stopped) setConnected(true) }
      catch { if (!stopped) setTimeout(() => { if (c.state === HubConnectionState.Disconnected && !stopped) void start() }, 5000) }
    }
    void start()
    const hb = setInterval(() => { if (c.state === HubConnectionState.Connected) c.invoke('Heartbeat').catch(() => {}) }, 25000)
    return () => { stopped = true; clearInterval(hb); void c.stop(); conn.current = null; setConnected(false) }
  }, [status, logout])

  const on = (event: string, h: Handler) => {
    if (!handlers.current.has(event)) handlers.current.set(event, new Set())
    handlers.current.get(event)!.add(h)
    return () => { handlers.current.get(event)?.delete(h) }
  }
  const invoke = async (method: string, ...args: unknown[]) => {
    if (conn.current?.state === HubConnectionState.Connected) await conn.current.invoke(method, ...args)
  }
  return <RtCtx.Provider value={{ connected, on, invoke }}>{children}</RtCtx.Provider>
}

export const useRealtime = () => useContext(RtCtx)
export function useRealtimeEvent<T>(event: string, handler: (payload: T) => void) {
  const { on } = useRealtime()
  const ref = useRef(handler)
  ref.current = handler
  useEffect(() => on(event, ((p: T) => ref.current(p)) as Handler), [event, on])
}
