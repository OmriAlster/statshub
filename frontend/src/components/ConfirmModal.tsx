import type { ReactNode } from 'react'
import { Busy } from './BouncingBall'
import Modal from './Modal'

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
    <Modal head={<h3>{title}</h3>} onClose={onCancel}>
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
    </Modal>
  )
}
