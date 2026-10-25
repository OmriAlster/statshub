import { useLeague } from '../leagues'

// Small pill marking something as connected to / synced from an external
// league (IBBA, ...), with that league's mark. Used next to a player's name
// (profile-level link) and on a game - TeamCrest's LeagueMark covers teams.
export default function LeagueBadge({ league }: { league?: string | null }) {
  const { name, logoUrl } = useLeague(league)
  return (
    <span className="ibba-badge" title={`Connected to ${name}`}>
      <img src={logoUrl} alt="" />
      {name}
    </span>
  )
}

// Just the league's mark, as on a team crest or next to a game's type.
export function LeagueMark({ league, className }: { league?: string | null; className: string }) {
  const { name, logoUrl } = useLeague(league)
  return <img className={className} src={logoUrl} alt="" title={`Synced from ${name}`} />
}

// The league's name, in running text ("Synced from IBBA").
export function LeagueName({ league }: { league?: string | null }) {
  return <>{useLeague(league).name}</>
}
