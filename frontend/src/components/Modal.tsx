import type { ReactNode } from 'react'

// The app's dialog: a backdrop that closes it, a panel, and a head with the
// title (anything - a heading, a subtitle, a step counter) and a close button.
// "editor" is the larger panel used for editing a game or picking teams.
export default function Modal({ head, onClose, children, kind = 'dialog', panelClassName, label }: {
  head: ReactNode
  onClose: () => void
  children: ReactNode
  kind?: 'dialog' | 'editor'
  panelClassName?: string
  label?: string
}) {
  const [backdrop, panel] = kind === 'editor'
    ? ['game-edit-modal-backdrop', 'game-edit-modal-panel']
    : ['modal-backdrop', 'modal-panel']
  return (
    <div className={backdrop} onClick={onClose}>
      <div className={panelClassName ? `${panel} ${panelClassName}` : panel} onClick={(e) => e.stopPropagation()} role="dialog" aria-label={label}>
        <div className="modal-head">
          <div className="modal-head-title">{head}</div>
          <button className="modal-close" onClick={onClose} aria-label="Close">
            <svg className="icon"><use href="#i-x" /></svg>
          </button>
        </div>
        {children}
      </div>
    </div>
  )
}
