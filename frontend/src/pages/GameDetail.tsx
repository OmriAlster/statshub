import { useEffect, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
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

  const load = async (gameId: number) => {
    try {
      setLoading(true)
      const { data } = await api.get<GameDto>(`/games/${gameId}`)
      setGame(data)
      setError(null)
    } catch {
      setError('Could not load this game.')
    } finally {
      setLoading(false)
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
