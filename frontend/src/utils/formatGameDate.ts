// Games are stored in UTC and every display here converts to the viewer's
// local time via the browser's own Date/Intl handling - never format a raw
// ISO string by hand.

// Date only, no time - for list rows, which lead with the opponent now and
// keep the time-of-day tucked inside the game's own detail view: "May 9, 2026"
export function formatGameDateOnly(iso: string) {
  return new Date(iso).toLocaleDateString('en-US', {
    month: 'long',
    day: 'numeric',
    year: 'numeric',
  })
}

// Full form for a game's own header: "Tuesday, September 1, 2026, 4:17 PM"
export function formatGameDateTimeFull(iso: string) {
  return new Date(iso).toLocaleString('en-US', {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

// Just the time, for slotting next to a date already shown elsewhere: "4:17 PM"
export function formatGameTime(iso: string) {
  return new Date(iso).toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' })
}
