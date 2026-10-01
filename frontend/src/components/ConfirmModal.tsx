import type { ReactNode } from 'react'
import { Busy } from './BouncingBall'

interface ConfirmModalProps {
  title: string
  message: ReactNode
  confirmLabel?: string
  busy?: boolean
  onConfirm: () => void
  onCancel: () => void
}

export default function ConfirmModal({ title, message, confirmLabel = 'Delete', busy = false, onConfirm, onCancel }: ConfirmModalProps) {
  return (
    <div className="modal-backdrop" onClick={onCancel}>
      <div className="modal-panel" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <div className="modal-head-title">
            <h3>{title}</h3>
          </div>
          <button className="modal-close" onClick={onCancel} aria-label="Close">
            <svg className="icon"><use href="#i-x" /></svg>
          </button>
        </div>
        <div className="modal-body" style={{ padding: '0.25rem 1.5rem 1.5rem' }}>
          <p style={{ margin: '0.75rem 0 1.25rem' }}>{message}</p>
          <div className="flex gap-1">
            <button className="end-game-btn" onClick={onConfirm} disabled={busy}>
              {busy ? <Busy>Deleting…</Busy> : confirmLabel}
            </button>
            <button className="nav-btn" onClick={onCancel} disabled={busy}>
              Cancel
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
