import type { ReactNode } from 'react'

// A one-line suggestion above the page: icon, title and a short line, an
// optional action button, and a close button.
export default function DismissibleBanner({ icon, title, children, action, onDismiss }: {
  icon: string
  title: string
  children: ReactNode
  action?: ReactNode
  onDismiss: () => void
}) {
  return (
    <div className="install-banner">
      <span className="install-banner-icon">{icon}</span>
      <div className="install-banner-text">
        <strong>{title}</strong>
        <span>{children}</span>
      </div>
      {action}
      <button className="install-banner-close" onClick={onDismiss} aria-label="Dismiss">
        <svg className="icon"><use href="#i-x" /></svg>
      </button>
    </div>
  )
}
