import { useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import type { CreateGameStatsDto, GameDto, GameStatsDto, GameType, PlayerDto, ScoresheetLineDto, UpdateGameDto } from '../api/types'
import { madeOf, sheetMismatch, sheetOff, show } from '../utils/statLines'
import { Busy } from './BouncingBall'
import ConfirmModal from './ConfirmModal'
import GameStatsEditor, { type SaveState } from './GameStatsEditor'
import Modal from './Modal'
import NumbersEditor from './NumbersEditor'

type View = 'overview' | 'numbers' | 'shots'

// <input type="datetime-local"> works in local time with no zone.
function toDatetimeLocal(iso: string) {
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

const shortDate = (iso: string) => new Date(iso).toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' })

const sheetSummary = (l: ScoresheetLineDto) => `#${l.number} · ${l.points} pts · ${l.twoPointers}×2 · ${l.threePointers}×3 · ${l.freeThrows} FT`

// Editing a game: its details (a game you added - a synced one's come from
// the league), and the player's box score - where the numbers come from, IBBA's
// scoresheet beside them, and the two ways to record them. Everything saves as
// it's changed; Done only closes.
export default function EditGameSheet({ player, game, onGameUpdated, onClose }: {
  player: PlayerDto
  game: GameDto
  onGameUpdated: (game: GameDto) => void
  onClose: () => void
}) {
  const [current, setCurrent] = useState(game)
  const [stats, setStats] = useState<GameStatsDto | null>(game.playerStats.find((s) => s.playerId === player.id) ?? null)
  const [view, setView] = useState<View>('overview')
  const [saveState, setSaveState] = useState<SaveState | null>(null)
  const [confirm, setConfirm] = useState<'clear' | 'sheet' | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const sheet = current.scoresheets?.find((l) => l.playerId === player.id) ?? null
  const name = player.firstName

  // The game again from the server - the list behind shows it too.
  const reload = async () => {
    const { data } = await api.get<GameDto>(`/games/${game.id}`, { params: { playerId: player.id } })
    setCurrent(data)
    setStats(data.playerStats.find((s) => s.playerId === player.id) ?? null)
    onGameUpdated(data)
  }

  const run = async (work: () => Promise<unknown>, failure: string) => {
    setBusy(true)
    setError(null)
    setSaveState('saving')
    try {
      await work()
      await reload()
      setSaveState('saved')
    } catch {
      setSaveState('error')
      setError(failure)
    } finally {
      setBusy(false)
      setConfirm(null)
    }
  }

  const startRecording = (next: View) => run(async () => {
    const empty: CreateGameStatsDto = {
      gameId: game.id, playerId: player.id,
      fieldGoalsMade: 0, fieldGoalsAttempted: 0, threePointersMade: 0, threePointersAttempted: 0, freeThrowsMade: 0, freeThrowsAttempted: 0,
      offensiveRebounds: 0, defensiveRebounds: 0, assists: 0, steals: 0, blocks: 0, turnovers: 0, fouls: 0, minutesPlayed: 0,
    }
    await api.post('/gamestats', empty)
    setView(next)
  }, 'Could not start the box score.')

  const useSheet = () => run(() => api.post('/gamestats/from-scoresheet', { gameId: game.id, playerId: player.id }), "Could not bring back IBBA's stats.")
  const clearStats = () => run(() => api.delete(`/gamestats/${stats!.id}`), 'Could not clear the stats.')

  const backToOverview = async () => {
    setView('overview')
    try { await reload() } catch { /* the list refreshes on close anyway */ }
  }

  const heading = view === 'overview'
    ? <GameHeading game={current} />
    : (
      <div className="eg-subhead">
        <button className="eg-back" onClick={backToOverview} aria-label="Back to the box score">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M15 5l-7 7 7 7" /></svg>
        </button>
        <div>
          <h3><bdi>{name}</bdi>{view === 'numbers' ? "'s numbers" : "'s shot chart"}</h3>
          <p className="eg-sub">vs <bdi>{current.opponentName}</bdi> · {shortDate(current.gameDate)}</p>
        </div>
      </div>
    )

  return (
    <Modal kind="editor" onClose={onClose} head={heading} label={`Edit game vs ${current.opponentName}`}>
      <div className="eg-body">
        {view === 'overview' && (
          <>
            {!current.isFromIbba && <DetailsCard game={current} playerId={player.id} onSaved={(g) => { setCurrent(g); onGameUpdated(g) }} onSaveState={setSaveState} />}
            <BoxScoreCard
              name={name}
              stats={stats}
              sheet={sheet}
              busy={busy}
              onRecord={startRecording}
              onEdit={setView}
              onUseSheet={() => (stats ? setConfirm('sheet') : useSheet())}
            />
            {stats && (
              <button className="eg-danger" onClick={() => setConfirm('clear')} disabled={busy}>Clear <bdi>{name}</bdi>'s stats for this game</button>
            )}
          </>
        )}
        {view === 'numbers' && stats && <NumbersEditor stats={stats} onSaved={setStats} onSaveState={setSaveState} />}
        {view === 'shots' && stats && <GameStatsEditor gameStatsId={stats.id} initialStats={stats} onSaveState={setSaveState} />}
        {error && <p className="error">{error}</p>}
      </div>

      <div className="eg-footer">
        <SaveStatus state={saveState} />
        <button className="eg-done" onClick={onClose}>Done</button>
      </div>

      {confirm === 'clear' && (
        <ConfirmModal
          title="Clear the stats?"
          message={<>The box score and shot chart <bdi>{name}</bdi> has for this game are deleted.</>}
          confirmLabel="Clear stats"
          busy={busy}
          onConfirm={clearStats}
          onCancel={() => setConfirm(null)}
        />
      )}
      {confirm === 'sheet' && sheet && (
        <ConfirmModal
          title="Use IBBA's stats?"
          message={`The box score becomes IBBA's scoresheet (${sheetSummary(sheet)}). What was recorded for this game, shot chart included, is replaced.`}
          confirmLabel="Use IBBA's stats"
          busy={busy}
          onConfirm={useSheet}
          onCancel={() => setConfirm(null)}
        />
      )}
    </Modal>
  )
}

function GameHeading({ game }: { game: GameDto }) {
  const done = game.teamScore != null && game.opponentScore != null
  const won = done && game.teamScore! > game.opponentScore!
  return (
    <div>
      <h3 className="eg-title">vs <bdi>{game.opponentName}</bdi></h3>
      <p className="eg-sub">
        <span>{shortDate(game.gameDate)}</span>
        <span>·</span>
        <span>{game.gameType}</span>
        {done && <><span>·</span><span className={`eg-result ${won ? 'win' : 'loss'}`}>{won ? 'W' : 'L'} {game.teamScore}–{game.opponentScore}</span></>}
        {game.isFromIbba && <span className="eg-synced">Synced from IBBA</span>}
      </p>
    </div>
  )
}

function SaveStatus({ state }: { state: SaveState | null }) {
  if (state === 'saving') return <span className="eg-saved">Saving…</span>
  if (state === 'error') return <span className="eg-saved error">Couldn't save - check your connection</span>
  if (state === 'saved') {
    return (
      <span className="eg-saved">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="var(--color-success)" strokeWidth="2.4" strokeLinecap="round" aria-hidden="true"><path d="M5 12l5 5 9-10" /></svg>
        Saved
      </span>
    )
  }
  return <span className="eg-saved">Changes save as you go</span>
}

function BoxScoreCard({ name, stats, sheet, busy, onRecord, onEdit, onUseSheet }: {
  name: string
  stats: GameStatsDto | null
  sheet: ScoresheetLineDto | null
  busy: boolean
  onRecord: (view: View) => void
  onEdit: (view: View) => void
  onUseSheet: () => void
}) {
  const mismatch = sheetMismatch(stats ?? undefined)
  const off = sheetOff(stats ?? undefined)
  const chip = !stats ? <span className="eg-chip none">Not recorded</span>
    : mismatch ? <span className="eg-chip diff"><span className="eg-bang" aria-hidden="true">!</span>Differs from IBBA</span>
    : stats.source ? <span className="eg-chip ibba">From IBBA{sheet ? ` · #${sheet.number}` : ''}</span>
    : <span className="eg-chip mine">Yours</span>

  return (
    <section className="eg-card" aria-label={`${name}'s box score`}>
      <div className="eg-cardhd"><span className="eg-label"><bdi>{name}</bdi>'s box score</span>{chip}</div>

      {!stats ? (
        <>
          {sheet && (
            <div className="eg-sheetline">
              <span className="eg-label ibba">IBBA's scoresheet · #{sheet.number}</span>
              <div className="eg-nums">
                <Num value={sheet.points} label="PTS" />
                <Num value={sheet.twoPointers} label="2PT" />
                <Num value={sheet.threePointers} label="3PT" />
                <Num value={sheet.freeThrows} label="FT" />
              </div>
              <p className="eg-muted">Made shots and fouls ({sheet.fouls}) only. Misses, rebounds, assists and minutes stay "-" until you add them.</p>
              {!sheet.sheetValid && <p className="eg-muted warn">The scoresheet doesn't add up, so its numbers may be off.</p>}
              <button className="eg-btn ibba" onClick={onUseSheet} disabled={busy}>{busy ? <Busy>Working…</Busy> : "Use IBBA's stats"}</button>
            </div>
          )}
          {sheet && <div className="eg-or">or record it yourself</div>}
          <button className="eg-choice" onClick={() => onRecord('numbers')} disabled={busy}>
            <span className="eg-ic" aria-hidden="true"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M4 7h16M4 12h16M4 17h10" /></svg></span>
            <span><b>Type the numbers</b><small>Points, shots, rebounds, minutes - from a stat sheet or memory</small></span>
          </button>
          <button className="eg-choice" onClick={() => onRecord('shots')} disabled={busy}>
            <span className="eg-ic" aria-hidden="true"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="8" /><circle cx="12" cy="12" r="2.5" /></svg></span>
            <span><b>Mark shots on the court</b><small>Where each shot was taken - builds the shot chart</small></span>
          </button>
        </>
      ) : (
        <>
          <div className="eg-nums">
            <Num value={stats.totalPoints} label="PTS" className={off('points')} />
            <Num value={madeOf(stats.fieldGoalsMade, stats.fieldGoalsAttempted)} label="2PT" className={off('two')} />
            <Num value={madeOf(stats.threePointersMade, stats.threePointersAttempted)} label="3PT" className={off('three')} />
            <Num value={madeOf(stats.freeThrowsMade, stats.freeThrowsAttempted)} label="FT" className={off('ft')} />
            <Num value={show(stats.totalRebounds)} label="REB" />
            <Num value={show(stats.assists)} label="AST" />
            <Num value={stats.fouls} label="FOULS" className={off('fouls')} />
            <Num value={show(stats.minutesPlayed)} label="MIN" />
          </div>

          {mismatch && sheet && (
            <div className="eg-cmp" role="table" aria-label="Your numbers against IBBA's scoresheet">
              <div className="eg-cr head" role="row"><span role="columnheader">Scoresheet #{sheet.number}</span><span role="columnheader">Yours</span><span role="columnheader">IBBA</span></div>
              {mismatch.map((r) => (
                <div key={r.stat} className={`eg-cr${r.differs ? ' off' : ''}`} role="row">
                  <span role="rowheader">{r.label}</span><span role="cell">{r.recorded}</span><span role="cell">{r.sheet}</span>
                </div>
              ))}
            </div>
          )}
          {stats.source === 'scoresheet' && (
            <p className="eg-hint">The scoresheet only has made shots and fouls. Add misses, rebounds or minutes and the game becomes yours - IBBA won't change it after that.</p>
          )}
          {stats.source === 'box' && <p className="eg-hint">From IBBA's box score. Correct anything - minutes, say - and the game becomes yours.</p>}
          {sheet && !sheet.sheetValid && <p className="eg-hint warn">IBBA's scoresheet doesn't add up, so its numbers may be off.</p>}

          <div className="eg-btns">
            <button className={`eg-btn${stats.source === 'scoresheet' ? ' pri' : ''}`} onClick={() => onEdit('numbers')}>
              {stats.source === 'scoresheet' ? "Add what's missing" : 'Edit numbers'}
            </button>
            <button className="eg-btn" onClick={() => onEdit('shots')}>Shot chart</button>
          </div>
          {sheet && stats.source !== 'scoresheet' && (
            <button className="eg-btn ghost-ibba" onClick={onUseSheet} disabled={busy}>Use IBBA's stats instead</button>
          )}
        </>
      )}
    </section>
  )
}

function Num({ value, label, className = '' }: { value: number | string; label: string; className?: string }) {
  return <div className={`eg-n ${className}`}><b>{value}</b><span>{label}</span></div>
}

interface DetailsForm {
  opponentName: string
  gameDate: string
  location: string
  gameType: GameType
  isHomeGame: boolean
  teamScore: string
  opponentScore: string
}

// A game you added: its details save themselves a moment after each change.
function DetailsCard({ game, playerId, onSaved, onSaveState }: { game: GameDto; playerId: string; onSaved: (g: GameDto) => void; onSaveState: (s: SaveState) => void }) {
  const [form, setForm] = useState<DetailsForm>({
    opponentName: game.opponentName,
    gameDate: toDatetimeLocal(game.gameDate),
    location: game.location,
    gameType: game.gameType,
    isHomeGame: game.isHomeGame !== false,
    teamScore: game.teamScore != null ? String(game.teamScore) : '',
    opponentScore: game.opponentScore != null ? String(game.opponentScore) : '',
  })
  const timer = useRef<number | null>(null)
  // What the server has - a save happens only when the form differs from it.
  const savedForm = useRef(JSON.stringify(form))

  useEffect(() => {
    if (JSON.stringify(form) === savedForm.current) return
    if (!form.opponentName.trim() || Number.isNaN(new Date(form.gameDate).getTime())) return
    if (timer.current) window.clearTimeout(timer.current)
    onSaveState('saving')
    timer.current = window.setTimeout(async () => {
      const teamScore = form.teamScore === '' ? null : Number(form.teamScore)
      const opponentScore = form.opponentScore === '' ? null : Number(form.opponentScore)
      const dto: UpdateGameDto = {
        opponentName: form.opponentName.trim(),
        gameDate: new Date(form.gameDate).toISOString(),
        location: form.location.trim(),
        gameType: form.gameType,
        teamScore,
        opponentScore,
        status: teamScore != null && opponentScore != null ? 'Completed' : 'Upcoming',
        isHomeGame: form.isHomeGame,
      }
      try {
        const { data } = await api.put<GameDto>(`/games/${game.id}`, dto, { params: { playerId } })
        savedForm.current = JSON.stringify(form)
        onSaved(data)
        onSaveState('saved')
      } catch {
        onSaveState('error')
      }
    }, 700)
    return () => { if (timer.current) window.clearTimeout(timer.current) }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form])

  const set = <K extends keyof DetailsForm>(key: K, value: DetailsForm[K]) => setForm((f) => ({ ...f, [key]: value }))

  return (
    <section className="eg-card" aria-label="Game details">
      <div className="eg-cardhd"><span className="eg-label">Game details</span></div>
      <div className="eg-fields">
        <label className="eg-f wide">Opponent<input value={form.opponentName} onChange={(e) => set('opponentName', e.target.value)} /></label>
        <label className="eg-f">Date &amp; time<input type="datetime-local" value={form.gameDate} onChange={(e) => set('gameDate', e.target.value)} /></label>
        <label className="eg-f">Location<input value={form.location} onChange={(e) => set('location', e.target.value)} /></label>
        <div className="eg-f">Home / away
          <div className="eg-seg" role="group" aria-label="Home or away">
            <button className={form.isHomeGame ? 'on' : ''} aria-pressed={form.isHomeGame} onClick={() => set('isHomeGame', true)}>Home</button>
            <button className={!form.isHomeGame ? 'on' : ''} aria-pressed={!form.isHomeGame} onClick={() => set('isHomeGame', false)}>Away</button>
          </div>
        </div>
        <div className="eg-f">Type
          <div className="eg-seg" role="group" aria-label="Game type">
            {(['League', 'Cup', 'Friendly'] as const).map((t) => (
              <button key={t} className={form.gameType === t ? 'on' : ''} aria-pressed={form.gameType === t} onClick={() => set('gameType', t)}>{t}</button>
            ))}
          </div>
        </div>
        <div className="eg-f wide">Final score · {game.teamName || 'us'} – opponent
          <div className="eg-score">
            <input type="number" inputMode="numeric" value={form.teamScore} onChange={(e) => set('teamScore', e.target.value)} aria-label="Our score" />
            <span>–</span>
            <input type="number" inputMode="numeric" value={form.opponentScore} onChange={(e) => set('opponentScore', e.target.value)} aria-label="Opponent's score" />
          </div>
        </div>
      </div>
    </section>
  )
}
