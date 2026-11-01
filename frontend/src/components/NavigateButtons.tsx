// Waze / Google Maps to a game's hall - the address as the league (or the
// parent) wrote it, e.g. "אולם ביהס יגאל אלון 8, רמת אלון- חיפה".
// iconOnly: just the two icons, small (a row that has no room for words) -
// the words are still there, for CSS to show where it has the room.
export default function NavigateButtons({ location, compact = false, iconOnly = false }: { location?: string | null; compact?: boolean; iconOnly?: boolean }) {
  const address = location?.trim()
  if (!address || address.toUpperCase() === 'TBD') return null
  const q = encodeURIComponent(address)
  return (
    <div className={`navigate-buttons ${compact ? 'compact' : ''} ${iconOnly ? 'icon-only' : ''}`}>
      <a className="navigate-btn waze" href={`https://waze.com/ul?q=${q}&navigate=yes`} target="_blank" rel="noopener noreferrer" aria-label={`Navigate with Waze to ${address}`} title="Waze">
        {iconOnly ? <>🚗<span className="navigate-word"> Waze</span></> : '🚗 Waze'}
      </a>
      <a className="navigate-btn maps" href={`https://www.google.com/maps/search/?api=1&query=${q}`} target="_blank" rel="noopener noreferrer" aria-label={`Open ${address} in Google Maps`} title="Google Maps">
        {iconOnly ? <>🗺️<span className="navigate-word"> Maps</span></> : '🗺️ Maps'}
      </a>
    </div>
  )
}
