import type { GamesView } from '../hooks/useGamesView'

// Cards or table, on a phone (hidden on wider screens, which show the table).
export default function GamesViewToggle({ view, onChange }: { view: GamesView; onChange: (view: GamesView) => void }) {
  return (
    <div className="games-view-toggle mobile-only">
      <div className="game-type-toggle" role="group" aria-label="Show games as">
        <button type="button" className={`toggle-option ${view === 'cards' ? 'active' : ''}`} aria-pressed={view === 'cards'} onClick={() => onChange('cards')}>
          <svg className="icon" aria-hidden="true" viewBox="0 0 24 24"><rect x="4" y="4" width="16" height="6" rx="2" /><rect x="4" y="14" width="16" height="6" rx="2" /></svg>
          Cards
        </button>
        <button type="button" className={`toggle-option ${view === 'table' ? 'active' : ''}`} aria-pressed={view === 'table'} onClick={() => onChange('table')}>
          <svg className="icon" aria-hidden="true" viewBox="0 0 24 24"><path d="M4 6h16M4 12h16M4 18h16M9 6v12" /></svg>
          Table
        </button>
      </div>
    </div>
  )
}
