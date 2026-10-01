import { useEffect, useRef, useState } from 'react'
import { usePushNotifications } from '../hooks/usePushNotifications'
import { Busy } from './BouncingBall'

// Header bell: shows whether notifications are on for THIS device, turns
// them on (a tap - iPhone only allows setting them up from one), and sends a
// test notification to check they actually arrive. Keeping the server in
// sync with this device's subscription is NotificationBanner's job (it runs
// once per page); this component only reads state and acts on taps.
export default function NotificationBell() {
  const { supported, permission, subscribed, busy, subscribe, sendTest, needsHomeScreenApp } = usePushNotifications()
  const [open, setOpen] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [sending, setSending] = useState(false)
  const wrapRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const close = (e: MouseEvent | TouchEvent) => {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('mousedown', close)
    document.addEventListener('touchstart', close)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', close)
      document.removeEventListener('touchstart', close)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  const on = supported && permission === 'granted' && subscribed

  const test = async () => {
    setSending(true)
    setMessage(null)
    const ok = await sendTest()
    setSending(false)
    setMessage(ok ? 'Test sent - it should arrive in a few seconds.' : "Couldn't send the test - try again.")
  }

  const turnOn = async () => {
    setMessage(null)
    const ok = await subscribe()
    if (ok) {
      await test()
    } else {
      setMessage(Notification.permission === 'denied'
        ? 'Notifications are blocked for StatsHub - allow them in your browser or phone settings.'
        : "Couldn't turn notifications on - try again.")
    }
  }

  let status: string
  if (!supported) {
    status = needsHomeScreenApp
      ? 'On iPhone, notifications only work when StatsHub is opened from its home-screen icon.'
      : "This browser doesn't support notifications."
  } else if (permission === 'denied') {
    status = 'Notifications are blocked for StatsHub - allow them in your browser or phone settings.'
  } else if (on) {
    status = 'Notifications are on for this device.'
  } else {
    status = 'Notifications are off for this device.'
  }

  return (
    <div className="notif-bell-wrap" ref={wrapRef}>
      <button
        className={`logout-btn notif-bell ${on ? 'is-on' : ''}`}
        onClick={() => { setOpen((v) => !v); setMessage(null) }}
        aria-label={on ? 'Notifications on' : 'Notifications off'}
        aria-expanded={open}
        title="Notifications"
      >
        <svg className="icon"><use href="#i-bell" /></svg>
        {on && <span className="notif-bell-dot" aria-hidden="true" />}
      </button>

      {open && (
        <div className="notif-panel" role="dialog" aria-label="Notifications">
          <strong className="notif-panel-title">Notifications</strong>
          <p className="notif-panel-status">{status}</p>
          {supported && permission !== 'denied' && (
            on ? (
              <button className="submit-btn notif-panel-btn" onClick={test} disabled={sending}>
                {sending ? <Busy>Sending…</Busy> : 'Send test notification'}
              </button>
            ) : (
              <button className="submit-btn notif-panel-btn" onClick={turnOn} disabled={busy || sending}>
                {busy ? <Busy>Turning on…</Busy> : 'Turn on notifications'}
              </button>
            )
          )}
          {message && <p className="notif-panel-message" role="status">{message}</p>}
        </div>
      )}
    </div>
  )
}
