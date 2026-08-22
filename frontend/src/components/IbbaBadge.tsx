// Small pill marking something as connected to / sourced from IBBA. Used next
// to a player's name (profile-level link) and inline in games tables
// (per-game sync indicator) - the crest overlay mark on TeamCrest covers the
// team-level case.
export default function IbbaBadge({ label = 'IBBA' }: { label?: string }) {
  return (
    <span className="ibba-badge" title="Connected to IBBA">
      <img src="/icons/ibba-logo.png" alt="" />
      {label}
    </span>
  )
}
