import { useEffect, useRef, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import { useDataRefresh } from '../api/dataSync'
import type { GameDto } from '../api/types'
import GameDetailView from '../components/GameDetailView'

export default function GameDetail() {
  const { id } = useParams<{ id: string }>()
  const [searchParams] = useSearchParams()
  const playerIdParam = searchParams.get('playerId')
  const [game, setGame] = useState<GameDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [sharing, setSharing] = useState(false)
  const [shareUrl, setShareUrl] = useState<string | null>(null)
  const [shareError, setShareError] = useState<string | null>(null)

  useEffect(() => {
    if (id) load(Number(id))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id])

  // Score, status, box score and shot chart can all change from elsewhere
  // (the live widget finishing the game, an IBBA sync filling in a final
  // score) while this page is open.
  useDataRefresh(['games', 'stats', 'shots', 'ibba'], () => {
    if (id) load(Number(id), true)
  })

  const loadSeq = useRef(0)

  const load = async (gameId: number, silent = false) => {
    const seq = ++loadSeq.current
    try {
      if (!silent) setLoading(true)
      const { data } = await api.get<GameDto>(`/games/${gameId}`)
      if (seq !== loadSeq.current) return
      setGame(data)
      setError(null)
    } catch {
      // A silent refresh failing (e.g. the game was just deleted elsewhere)
      // keeps showing what's already here rather than blanking the page.
      if (!silent) setError('Could not load this game.')
    } finally {
      if (!silent) setLoading(false)
    }
  }

  const shareGame = async () => {
    if (!game) return
    // A game synced from IBBA may not have a box score yet, so there's no
    // GameStats row to read a playerId off of - fall back to whichever
    // player's Stats page this game was opened from.
    const playerId = game.playerStats[0]?.playerId ?? (playerIdParam ? Number(playerIdParam) : null)
    if (!playerId) {
      setShareError('Could not determine which player to share this game as.')
      return
    }
    setSharing(true)
    setShareError(null)
    try {
      const { data } = await api.post('/share', { playerId, gameId: game.id })
      setShareUrl(`${window.location.origin}/share/${data.token}`)
      try {
        await navigator.clipboard.writeText(`${window.location.origin}/share/${data.token}`)
      } catch {
        // clipboard may be unavailable
      }
    } catch {
      setShareError('Could not create a share link.')
    } finally {
      setSharing(false)
    }
  }

  if (loading) {
    return (
      <div className="page-container">
        <h2>📅 Game Detail</h2>
        <p>Loading...</p>
      </div>
    )
  }

  if (error || !game) {
    return (
      <div className="page-container">
        <h2>📅 Game Detail</h2>
        <p className="error">{error ?? 'Game not found.'}</p>
        <Link to="/stats">← Back to Profiles</Link>
      </div>
    )
  }

  return (
    <div className="page-container">
      <Link to="/stats" className="back-link">← Back to Profiles</Link>

      {shareError && <p className="error">{shareError}</p>}

      {shareUrl && (
        <div className="invite-box">
          <p>Share link (copied to clipboard):</p>
          <code>{shareUrl}</code>
        </div>
      )}

      <GameDetailView
        game={game}
        headerActions={
          <button className="submit-btn" onClick={shareGame} disabled={sharing}>
            {sharing ? 'Creating link...' : '🔗 Share This Game'}
          </button>
        }
      />
    </div>
  )
}
