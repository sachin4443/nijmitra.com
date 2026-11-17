/// <reference lib="webworker" />
import { cleanupOutdatedCaches, precacheAndRoute, createHandlerBoundToURL } from 'workbox-precaching'
import { NavigationRoute, registerRoute } from 'workbox-routing'

declare const self: ServiceWorkerGlobalScope & { __WB_MANIFEST: Array<{ url: string; revision: string | null }> }

// App shell only: built JS/CSS/HTML/icons. API responses, files, vault data, diary and chat are NEVER cached.
cleanupOutdatedCaches()
precacheAndRoute(self.__WB_MANIFEST)
registerRoute(new NavigationRoute(createHandlerBoundToURL('/index.html'), { denylist: [/^\/api\//, /^\/hubs\//, /^\/swagger/] }))

self.addEventListener('message', (event) => {
  if (event.data?.type === 'SKIP_WAITING') self.skipWaiting()
  if (event.data?.type === 'LOGOUT') {
    // Defensive: remove any runtime caches (none are created for private data, but keep logout airtight).
    event.waitUntil(caches.keys().then((keys) => Promise.all(keys.filter((k) => !k.startsWith('workbox-precache')).map((k) => caches.delete(k)))))
  }
})

self.addEventListener('push', (event) => {
  let data: { title?: string; body?: string; url?: string; tag?: string; icon?: string; badge?: string } = {}
  try { data = event.data?.json() ?? {} } catch { data = { body: event.data?.text() } }
  event.waitUntil(self.registration.showNotification(data.title || 'NijMitra', {
    body: data.body || '',
    icon: data.icon || '/branding/pwa-192.png',
    badge: data.badge || '/branding/favicon-96.png',
    tag: data.tag,
    data: { url: data.url || '/' }
  }))
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const target = new URL(event.notification.data?.url || '/', self.location.origin).href
  event.waitUntil((async () => {
    const all = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })
    for (const c of all) { if ('focus' in c) { await c.focus(); (c as WindowClient).navigate(target); return } }
    await self.clients.openWindow(target)
  })())
})
