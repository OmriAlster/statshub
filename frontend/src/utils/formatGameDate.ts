// Games are stored in UTC and every display here converts to the viewer's
// local time via the browser's own Date/Intl handling - never format a raw
// ISO string by hand.

// Compact form for table rows: "Sep 1, 4:17 PM"
export function formatGameDateTime(iso: string) {
  return new Date(iso).toLocaleString('en-US', {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
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
