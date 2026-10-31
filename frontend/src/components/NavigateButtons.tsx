// Waze / Google Maps to a game's hall - the address as the league (or the
// parent) wrote it, e.g. "אולם ביהס יגאל אלון 8, רמת אלון- חיפה".
export default function NavigateButtons({ location, compact = false }: { location?: string | null; compact?: boolean }) {
  const address = location?.trim()
  if (!address || address.toUpperCase() === 'TBD') return null
  const q = encodeURIComponent(address)
  return (
    <div className={`navigate-buttons ${compact ? 'compact' : ''}`}>
      <a className="navigate-btn waze" href={`https://waze.com/ul?q=${q}&navigate=yes`} target="_blank" rel="noopener noreferrer" aria-label={`Navigate with Waze to ${address}`}>
        🚗 Waze
      </a>
      <a className="navigate-btn maps" href={`https://www.google.com/maps/search/?api=1&query=${q}`} target="_blank" rel="noopener noreferrer" aria-label={`Open ${address} in Google Maps`}>
        🗺️ Maps
      </a>
    </div>
  )
}
