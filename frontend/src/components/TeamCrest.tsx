import { LeagueMark } from './LeagueBadge'

interface TeamCrestProps {
  logoUrl?: string | null
  jerseyNumber?: number | null
  // The league the team is synced from (its key) - shows that league's mark.
  league?: string | null
  size?: 'sm' | 'md'
  onClick?: () => void
  title?: string
}

// The crest + per-team jersey number + league sync mark, as one interactive
// unit - a team's identity everywhere it shows up (Dashboard, Player
// Profile). Clicking it is meant to open that team's standings.
export default function TeamCrest({ logoUrl, jerseyNumber, league, size = 'md', onClick, title }: TeamCrestProps) {
  const Tag = onClick ? 'button' : 'div'
  return (
    <Tag
      className={`crest-cluster ${size === 'sm' ? 'crest-cluster-sm' : ''}`}
      onClick={onClick}
      type={onClick ? 'button' : undefined}
      title={title}
      style={onClick ? { background: 'none', border: 'none', padding: 0 } : undefined}
    >
      {logoUrl ? (
        <img className="crest-main" src={logoUrl} alt="" />
      ) : (
        <div className="crest-main" />
      )}
      {jerseyNumber != null && <span className="cc-jersey">{jerseyNumber}</span>}
      {league && (
        <span className="cc-ibba">
          <LeagueMark league={league} className="" />
        </span>
      )}
    </Tag>
  )
}
