import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'

// The VAPID public key comes from the server as URL-safe base64; PushManager
// wants it as a raw Uint8Array.
function urlBase64ToUint8Array(base64: string) {
  const padding = '='.repeat((4 - (base64.length % 4)) % 4)
  const normalized = (base64 + padding).replace(/-/g, '+').replace(/_/g, '/')
  const raw = window.atob(normalized)
  return Uint8Array.from([...raw].map((c) => c.charCodeAt(0)))
}

function isIOS() {
  return /iphone|ipad|ipod/i.test(window.navigator.userAgent)
}

function isStandalone() {
  return (
    window.matchMedia?.('(display-mode: standalone)').matches ||
    (window.navigator as Navigator & { standalone?: boolean }).standalone === true
  )
}

// userId: the signed-in account - the subscription is re-registered to it
// whenever it changes (see the effect below).
export function usePushNotifications(userId?: number) {
  // iOS only exposes working Notification/PushManager APIs once the site is
  // added to the home screen - in a regular Safari tab they're either
  // missing or silently non-functional, so treat push as unsupported there
  // until the app is running standalone. Every other platform (desktop and
  // Android Chrome/Edge/Firefox) works fine in an ordinary browser tab.
  const [supported] = useState(
    () => 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window && (!isIOS() || isStandalone())
  )
  const [permission, setPermission] = useState<NotificationPermission>(() => (supported ? Notification.permission : 'denied'))
  const [subscribed, setSubscribed] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!supported) return
    navigator.serviceWorker.ready
      .then(async (registration) => {
        const existing = await registration.pushManager.getSubscription()
        // Allowed but never set up (permission granted earlier, or the
        // browser dropped its subscription): the Enable banner only shows
        // while permission is undecided, so nothing would ever set it up -
        // do it now. No prompt: the permission is already granted.
        if (!existing && userId != null && Notification.permission === 'granted') {
          const { data: publicKey } = await api.get<string>('/push/vapid-public-key')
          return registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: urlBase64ToUint8Array(publicKey) })
        }
        return existing
      })
      .then((subscription) => {
        setSubscribed(!!subscription)
        // The phone remembering its subscription doesn't mean the server
        // still has it - the production database was wiped once (deleting
        // every subscription while every phone still thought it was
        // subscribed, so nobody got notified again and the Enable banner
        // never came back), and logging into another account on the same
        // phone left the subscription pointing at the old account. So every
        // time the app opens signed in with notifications allowed, send the
        // subscription again; the server keeps one row per phone and just
        // points it at the current account.
        if (subscription && userId != null && Notification.permission === 'granted') {
          const json = subscription.toJSON()
          api
            .post('/push/subscribe', { endpoint: json.endpoint, p256dh: json.keys?.p256dh, auth: json.keys?.auth })
            .catch(() => {})
        }
      })
      .catch(() => {})
  }, [supported, userId])

  const subscribe = useCallback(async () => {
    if (!supported) return false
    setBusy(true)
    try {
      const result = await Notification.requestPermission()
      setPermission(result)
      if (result !== 'granted') return false

      const { data: publicKey } = await api.get<string>('/push/vapid-public-key')
      const registration = await navigator.serviceWorker.ready
      const subscription = await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: urlBase64ToUint8Array(publicKey),
      })
      const json = subscription.toJSON()
      await api.post('/push/subscribe', {
        endpoint: json.endpoint,
        p256dh: json.keys?.p256dh,
        auth: json.keys?.auth,
      })
      setSubscribed(true)
      return true
    } catch {
      return false
    } finally {
      setBusy(false)
    }
  }, [supported])

  const unsubscribe = useCallback(async () => {
    if (!supported) return
    setBusy(true)
    try {
      const registration = await navigator.serviceWorker.ready
      const subscription = await registration.pushManager.getSubscription()
      if (subscription) {
        await api.post('/push/unsubscribe', { endpoint: subscription.endpoint }).catch(() => {})
        await subscription.unsubscribe()
      }
      setSubscribed(false)
    } finally {
      setBusy(false)
    }
  }, [supported])

  // Asks the server to push a test notification to every device of the
  // signed-in account - the Settings bell's "Send test notification".
  const sendTest = useCallback(async () => {
    try {
      await api.post('/push/test')
      return true
    } catch {
      return false
    }
  }, [])

  // iPhone in a regular Safari tab: web notifications only exist once the
  // app is opened from its home-screen icon.
  const needsHomeScreenApp = isIOS() && !isStandalone()

  return { supported, permission, subscribed, busy, subscribe, unsubscribe, sendTest, needsHomeScreenApp }
}
