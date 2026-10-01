import { useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { usePushNotifications } from '../hooks/usePushNotifications'
import { Busy } from './BouncingBall'

const DISMISSED_KEY = 'statshub_notification_banner_dismissed'

export default function NotificationBanner() {
  const { user } = useAuth()
  const { supported, permission, subscribed, busy, subscribe } = usePushNotifications(user?.id)
  const [dismissed, setDismissed] = useState(() => localStorage.getItem(DISMISSED_KEY) === '1')

  const dismiss = () => {
    localStorage.setItem(DISMISSED_KEY, '1')
    setDismissed(true)
  }

  if (!supported || permission !== 'default' || subscribed || dismissed) return null

  const enable = async () => {
    const granted = await subscribe()
    // Denied permission flips `permission` to 'denied', which already hides
    // this banner on its own - only need to persist the dismissal on success.
    if (granted) dismiss()
  }

  return (
    <div className="install-banner">
      <span className="install-banner-icon">🔔</span>
      <div className="install-banner-text">
        <strong>Get game notifications</strong>
        <span>New games, live starts, and final scores - straight to your phone.</span>
      </div>
      <button className="install-banner-btn" onClick={enable} disabled={busy}>
        {busy ? <Busy>Enabling…</Busy> : 'Enable'}
      </button>
      <button className="install-banner-close" onClick={dismiss} aria-label="Dismiss">
        <svg className="icon"><use href="#i-x" /></svg>
      </button>
    </div>
  )
}
