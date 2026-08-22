interface TeamCrestProps {
  logoUrl?: string | null
  jerseyNumber?: number | null
  showIbbaMark?: boolean
  size?: 'sm' | 'md'
  onClick?: () => void
  title?: string
}

// The crest + per-team jersey number + IBBA sync mark, as one interactive
// unit - a team's identity everywhere it shows up (Dashboard, Player
// Profile). Clicking it is meant to open that team's standings.
export default function TeamCrest({ logoUrl, jerseyNumber, showIbbaMark, size = 'md', onClick, title }: TeamCrestProps) {
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
      {showIbbaMark && (
        <span className="cc-ibba" title="Synced from IBBA">
          <img src="/icons/ibba-logo.png" alt="" />
        </span>
      )}
    </Tag>
  )
}
