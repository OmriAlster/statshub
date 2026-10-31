import { useEffect, useMemo, useState } from 'react'
import type { GameDto, GameStatsDto, PlayerDto } from '../api/types'
import { LeagueSeason } from '../utils/season'
import { drawPlayerCard, drawWrapped, isolate, save, shareOrSave } from '../utils/shareImage'
import BouncingBall from './BouncingBall'
import Modal from './Modal'
import { playedGames } from '../utils/countedGames'

export interface CardTeam {
  name: string
  logoUrl?: string | null
  leagueName?: string | null
  jerseyNumber?: number | null
  position?: number | null
  totalTeams?: number | null
}

const lineOf = (g: GameDto, playerId: string): GameStatsDto | undefined => g.playerStats.find((s) => s.playerId === playerId) ?? g.playerStats[0]
const fileSafe = (s: string) => s.replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '')
const ordinal = (n: number) => `${n}${n % 10 === 1 && n % 100 !== 11 ? 'st' : n % 10 === 2 && n % 100 !== 12 ? 'nd' : n % 10 === 3 && n % 100 !== 13 ? 'rd' : 'th'}`

function ImageSheet({ title, label, draw, fileName }: { title: string; label: string; draw: () => Promise<string>; fileName: string }) {
  const [url, setUrl] = useState<string | null>(null)
  const [failed, setFailed] = useState(false)
  const [note, setNote] = useState<string | null>(null)

  useEffect(() => {
    let live = true
    setUrl(null)
    draw().then((u) => live && setUrl(u)).catch(() => live && setFailed(true))
    return () => { live = false }
  }, [draw])

  return (
    <div className="share-sheet">
      {!url && !failed && <BouncingBall size="sm" label="Making the picture…" />}
      {failed && <p className="error">The picture couldn't be made on this device.</p>}
      {url && <img className="share-preview" src={url} alt={label} />}
      {url && (
        <div className="share-actions">
          <button type="button" className="submit-btn" onClick={async () => {
            const r = await shareOrSave(url, fileName, title)
            setNote(r === 'saved' ? 'Saved to your downloads' : null)
          }}>
            Share
          </button>
          <button type="button" className="nav-btn" onClick={() => { save(url, fileName); setNote('Saved to your downloads') }}>Save image</button>
        </div>
      )}
      {note && <p className="share-note" role="status">{note}</p>}
    </div>
  )
}

export function PlayerCardModal({ player, teams, games, onClose }: { player: PlayerDto; teams: CardTeam[]; games: GameDto[]; onClose: () => void }) {
  const draw = useMemo(() => () => {
    const played = playedGames(games)
    const lines = played.map((g) => lineOf(g, player.id)).filter((l): l is GameStatsDto => !!l)
    const per = (f: (l: GameStatsDto) => number) => (lines.reduce((s, l) => s + f(l), 0) / lines.length).toFixed(1)
    const wins = played.filter((g) => (g.teamScore ?? 0) > (g.opponentScore ?? 0)).length
    const team = teams.length === 1 ? teams[0] : null
    const jerseys = [...new Set(teams.map((t) => t.jerseyNumber).filter((n): n is number => !!n))]
    const numbers = lines.length > 0
      ? [{ value: per((l) => l.totalPoints), label: 'PPG' }, { value: per((l) => l.totalRebounds), label: 'RPG' }, { value: per((l) => l.assists), label: 'APG' }]
      : [
          { value: String(played.length), label: 'Games' },
          { value: `${wins}-${played.length - wins}`, label: 'Record' },
          { value: team?.position ? ordinal(team.position) : '-', label: team?.totalTeams ? `of ${team.totalTeams}` : 'Place' },
        ]
    return drawPlayerCard({
      playerName: `${player.firstName} ${player.lastName}`.trim(),
      initials: `${player.firstName[0] ?? ''}${player.lastName?.[0] ?? ''}`,
      photoUrl: player.profilePictureUrl,
      teams: teams.slice(0, 3).map((t) => ({ name: t.name, crestUrl: t.logoUrl, leagueName: t.leagueName })),
      jersey: jerseys.length === 1 ? jerseys[0] : null,
      season: `${LeagueSeason.label(new Date())} season`,
      numbers,
      note: lines.length > 0 ? `${played.length} game${played.length === 1 ? '' : 's'} · ${wins}-${played.length - wins}` : null,
    })
  }, [player, teams, games])

  return (
    <Modal kind="editor" head={<h3>🃏 Player card</h3>} onClose={onClose} label="Player card">
      <div className="edit-panel">
        <ImageSheet title={`${player.firstName}'s card`} label={`${player.firstName}'s player card`} draw={draw} fileName={`${fileSafe(player.firstName)}-card.png`} />
      </div>
    </Modal>
  )
}

type Period = { key: string; label: string; from: Date; to: Date }

function periods(games: GameDto[]): Period[] {
  const start = LeagueSeason.startOf(new Date())
  const months = new Map<string, Period>()
  for (const g of playedGames(games)) {
    const d = new Date(g.gameDate)
    if (d < start) continue
    const key = `${d.getFullYear()}-${d.getMonth()}`
    if (!months.has(key)) {
      months.set(key, { key, label: d.toLocaleDateString('en-US', { month: 'long' }), from: new Date(d.getFullYear(), d.getMonth(), 1), to: new Date(d.getFullYear(), d.getMonth() + 1, 1) })
    }
  }
  const list = [...months.values()].sort((a, b) => b.from.getTime() - a.from.getTime())
  return [{ key: 'season', label: `${LeagueSeason.label(new Date())} season`, from: start, to: new Date(8.64e15) }, ...list]
}

export function WrappedModal({ player, games, onClose }: { player: PlayerDto; games: GameDto[]; onClose: () => void }) {
  const options = useMemo(() => periods(games), [games])
  const [key, setKey] = useState(options[1]?.key ?? 'season')
  const period = options.find((p) => p.key === key) ?? options[0]

  const draw = useMemo(() => () => {
    const list = playedGames(games).filter((g) => { const d = new Date(g.gameDate); return d >= period.from && d < period.to })
    const wins = list.filter((g) => (g.teamScore ?? 0) > (g.opponentScore ?? 0))
    const lines = list.map((g) => ({ g, l: lineOf(g, player.id) })).filter((r): r is { g: GameDto; l: GameStatsDto } => !!r.l)
    const out: { label: string; value: string; detail?: string }[] = []
    if (wins.length) {
      const big = wins.reduce((b, g) => (g.teamScore! - g.opponentScore! > b.teamScore! - b.opponentScore! ? g : b))
      out.push({ label: 'Biggest win', value: `+${big.teamScore! - big.opponentScore!}`, detail: `${big.teamScore}-${big.opponentScore} vs ${isolate(big.opponentName)}` })
    }
    if (lines.length) {
      const best = lines.reduce((b, r) => (r.l.totalPoints > b.l.totalPoints ? r : b))
      const total = lines.reduce((s, r) => s + r.l.totalPoints, 0)
      out.push({ label: 'Best game', value: `${best.l.totalPoints} pts`, detail: `vs ${isolate(best.g.opponentName)}` })
      out.push({ label: 'Points', value: String(total), detail: `${(total / lines.length).toFixed(1)} a game` })
      const threes = lines.reduce((s, r) => s + r.l.threePointersMade, 0)
      if (threes) out.push({ label: '3-pointers', value: String(threes) })
      const reb = lines.reduce((s, r) => s + r.l.totalRebounds, 0)
      out.push({ label: 'Rebounds', value: String(reb), detail: `${(reb / lines.length).toFixed(1)} a game` })
    } else {
      const pf = list.reduce((s, g) => s + g.teamScore!, 0)
      const pa = list.reduce((s, g) => s + g.opponentScore!, 0)
      if (list.length) out.push({ label: 'Team points', value: String(pf), detail: `${(pf / list.length).toFixed(1)} a game · ${(pa / list.length).toFixed(1)} allowed` })
    }
    return drawWrapped({
      period: period.label,
      playerName: `${player.firstName} ${player.lastName}`.trim(),
      initials: `${player.firstName[0] ?? ''}${player.lastName?.[0] ?? ''}`,
      photoUrl: player.profilePictureUrl,
      games: list.length,
      wins: wins.length,
      lines: out,
    })
  }, [games, period, player])

  return (
    <Modal kind="editor" head={<h3>🎉 In numbers</h3>} onClose={onClose} label="Season summary">
      <div className="edit-panel">
        {options.length > 1 && (
          <div className="wrapped-periods" role="group" aria-label="Period">
            {options.map((p) => (
              <button type="button" key={p.key} className={`type-filter-chip ${p.key === period.key ? 'active' : ''}`} onClick={() => setKey(p.key)}>
                {p.label}
              </button>
            ))}
          </div>
        )}
        <ImageSheet title={`${player.firstName}: ${period.label}`} label={`${player.firstName}'s ${period.label} in numbers`} draw={draw} fileName={`${fileSafe(player.firstName)}-${fileSafe(period.label)}.png`} />
      </div>
    </Modal>
  )
}
