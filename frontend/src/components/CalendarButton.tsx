import { useState } from 'react'
import { api } from '../api/client'
import Modal from './Modal'
import { Busy } from './BouncingBall'

// "Add to calendar": the players' games in the phone's calendar, kept up to
// date by the calendar app itself (a game that moves or is cancelled changes
// there too).
export default function CalendarButton() {
  const [open, setOpen] = useState(false)
  const [link, setLink] = useState<{ url: string; webcalUrl: string } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const show = async () => {
    setOpen(true)
    if (link) return
    try {
      setLink((await api.get<{ url: string; webcalUrl: string }>('/calendar/link')).data)
    } catch {
      setError("Couldn't make your calendar link - try again.")
    }
  }

  const copy = async () => {
    if (!link) return
    try {
      await navigator.clipboard.writeText(link.url)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      setError('Copy failed - press and hold the link to copy it.')
    }
  }

  return (
    <>
      <button type="button" className="calendar-btn" onClick={show}>
        <svg className="icon" aria-hidden="true"><use href="#i-calendar" /></svg>
        Add to calendar
      </button>
      {open && (
        <Modal head={<h3>Games in your calendar</h3>} onClose={() => setOpen(false)} label="Add the games to your calendar">
          <div className="calendar-sheet">
            <p>Every game shows in your phone's calendar - with the hall's address - and updates there by itself when a game moves or is cancelled.</p>
            {error && <p className="error">{error}</p>}
            {!link && !error && <Busy>Making your link…</Busy>}
            {link && (
              <>
                <a className="calendar-option google" href={`https://calendar.google.com/calendar/r?cid=${encodeURIComponent(link.webcalUrl)}`} target="_blank" rel="noopener noreferrer">
                  Google Calendar
                </a>
                <a className="calendar-option apple" href={link.webcalUrl}>
                  iPhone / Apple Calendar
                </a>
                <button type="button" className="calendar-option copy" onClick={copy}>
                  {copied ? 'Copied ✓' : 'Copy the link (other calendars)'}
                </button>
                <p className="calendar-note">This link is private to your account - anyone who has it can see the games.</p>
              </>
            )}
          </div>
        </Modal>
      )}
    </>
  )
}
