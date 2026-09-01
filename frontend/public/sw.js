// Minimal service worker: makes the app installable and keeps the last-seen
// shell/assets available offline. Never touches /api/ — stat data must
// always come from the network so a parent never sees stale/wrong numbers.
const CACHE_NAME = 'statshub-shell-v1'

self.addEventListener('install', () => {
  self.skipWaiting()
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key))))
      .then(() => self.clients.claim())
  )
})

self.addEventListener('push', (event) => {
  if (!event.data) return
  let payload
  try {
    payload = event.data.json()
  } catch {
    return
  }

  const { title, body, url, gameDateIso } = payload

  // The server sends the game's raw UTC instant, never a formatted string -
  // it has no idea what timezone this device is in. {time}/{datetime} in the
  // body get swapped for the device's own local rendering right here.
  let finalBody = body
  if (gameDateIso && typeof finalBody === 'string') {
    const d = new Date(gameDateIso)
    if (finalBody.includes('{time}')) {
      finalBody = finalBody.replace('{time}', d.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' }))
    }
    if (finalBody.includes('{datetime}')) {
      finalBody = finalBody.replace(
        '{datetime}',
        d.toLocaleString('en-US', { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
      )
    }
  }

  event.waitUntil(
    self.registration.showNotification(title || 'StatsHub', {
      body: finalBody,
      icon: '/icons/icon-192.png',
      badge: '/icons/icon-192.png',
      data: { url: url || '/' },
    })
  )
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const url = event.notification.data?.url || '/'

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clients) => {
      for (const client of clients) {
        if (client.url.includes(self.location.origin) && 'focus' in client) {
          client.navigate(url)
          return client.focus()
        }
      }
      return self.clients.openWindow(url)
    })
  )
})

self.addEventListener('fetch', (event) => {
  const { request } = event
  if (request.method !== 'GET') return

  const url = new URL(request.url)
  if (url.origin !== self.location.origin) return
  if (url.pathname.startsWith('/api/')) return

  event.respondWith(
    fetch(request)
      .then((response) => {
        const copy = response.clone()
        caches.open(CACHE_NAME).then((cache) => cache.put(request, copy))
        return response
      })
      .catch(() => caches.match(request).then((cached) => cached || caches.match('/')))
  )
})
