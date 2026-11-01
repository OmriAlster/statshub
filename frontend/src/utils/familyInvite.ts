import type { FamilyInviteDto, InvitePlayerDto } from '../api/types'

// The link a parent sends: /i/{code}, on this site.
export const inviteUrl = (code: string) => `${window.location.origin}/i/${code}`

// "יואב", "יואב ונועה", "יואב, נועה ודן" - the Hebrew "and" sticks to a
// Hebrew name, and takes a hyphen before a name in English.
export function joinNames(names: string[]): string {
  if (names.length <= 1) return names[0] ?? ''
  const last = names[names.length - 1]
  const and = /^[֐-׿]/.test(last) ? 'ו' : 'ו-'
  return `${names.slice(0, -1).join(', ')} ${and}${last}`
}

// "Yoav", "Yoav and Noa", "Yoav, Noa and Dan" - for the app's own pages.
export function joinNamesEn(names: string[]): string {
  if (names.length <= 1) return names[0] ?? ''
  return `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`
}

// The WhatsApp message, ready to send (it can still be changed there).
export function inviteMessage(invite: Pick<FamilyInviteDto, 'kind' | 'code'> & { players: Pick<InvitePlayerDto, 'firstName'>[] }, from: string): string {
  const url = inviteUrl(invite.code)
  if (invite.kind === 'Player') {
    return `🏀 ${invite.players[0]?.firstName ?? ''}, זה הדף שלך ב-StatsHub. נכנסים מהקישור:\n${url}`
  }
  return `🏀 הזמנה מ-${from} לעקוב אחרי ${joinNames(invite.players.map((p) => p.firstName))} ב-StatsHub:\n${url}`
}

export const whatsAppHref = (text: string) => `https://wa.me/?text=${encodeURIComponent(text)}`

// The invite someone opened before signing in - accepted as soon as they're
// back from signing in (with Google, or on the sign-in page).
const PENDING_KEY = 'statshub:pending-invite'

export const pendingInvite = {
  get(): string | null {
    try { return sessionStorage.getItem(PENDING_KEY) } catch { return null }
  },
  set(code: string) {
    try { sessionStorage.setItem(PENDING_KEY, code) } catch { /* private mode - the page still works by hand */ }
  },
  clear() {
    try { sessionStorage.removeItem(PENDING_KEY) } catch { /* nothing to clear */ }
  },
}
