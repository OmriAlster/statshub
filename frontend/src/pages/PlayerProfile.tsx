import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { CreatePlayerFromIbbaResultDto, GameDto, IbbaLinkStatusDto, IbbaPreviewDto, InviteDto, PlayerDto, TeamDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import ConfirmModal from '../components/ConfirmModal'
import { OnCourtChip, onCourtIn } from '../components/GameStatusBadge'
import { useLiveRefresh } from '../hooks/useLiveRefresh'
import IbbaBadge from '../components/IbbaBadge'
import StandingsModal from '../components/StandingsModal'
import TeamCrest from '../components/TeamCrest'
import BouncingBall, { Busy } from '../components/BouncingBall'

const emptyForm = {
  firstName: '',
  lastName: '',
  position: '',
  dateOfBirth: new Date().toISOString().split('T')[0],
}

const emptyIbbaNewForm = {
  url: '',
}

export default function PlayerProfile() {
  const { user } = useAuth()
  const isPlayerRole = user?.role === 'Player'
  const [players, setPlayers] = useState<PlayerDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [showAddForm, setShowAddForm] = useState(false)
  const [form, setForm] = useState(emptyForm)
  const [saving, setSaving] = useState(false)

  const [showAddIbbaForm, setShowAddIbbaForm] = useState(false)
  const [ibbaNewForm, setIbbaNewForm] = useState(emptyIbbaNewForm)
  const [ibbaNewPreview, setIbbaNewPreview] = useState<IbbaPreviewDto | null>(null)
  const [ibbaNewBusy, setIbbaNewBusy] = useState(false)
  const [ibbaNewError, setIbbaNewError] = useState<string | null>(null)

  const [invites, setInvites] = useState<Record<string, InviteDto>>({})
  const [parentInvites, setParentInvites] = useState<Record<string, InviteDto>>({})
  const [shareLinks, setShareLinks] = useState<Record<string, string>>({})
  const [busyPlayerId, setBusyPlayerId] = useState<string | null>(null)

  const [teamPickerOpenFor, setTeamPickerOpenFor] = useState<string | null>(null)
  const [newTeamName, setNewTeamName] = useState('')
  const [pickerJerseyNumber, setPickerJerseyNumber] = useState('')
  const [teamBusy, setTeamBusy] = useState(false)
  const [jerseyEdits, setJerseyEdits] = useState<Record<string, string>>({})
  // Player whose IBBA teams need a "link existing or create new" choice.
  const [choiceFor, setChoiceFor] = useState<string | null>(null)
  const [renaming, setRenaming] = useState<{ playerId: string; teamId: string; value: string; busy: boolean } | null>(null)

  const [ibbaLinks, setIbbaLinks] = useState<Record<string, IbbaLinkStatusDto | null>>({})
  const [ibbaUrlInput, setIbbaUrlInput] = useState<Record<string, string>>({})
  const [ibbaPreview, setIbbaPreview] = useState<Record<string, IbbaPreviewDto | null>>({})
  const [ibbaBusy, setIbbaBusy] = useState<Record<string, boolean>>({})
  const [ibbaError, setIbbaError] = useState<Record<string, string | null>>({})
  const [standingsFor, setStandingsFor] = useState<{ leagueUrl: string; leagueName: string; teamName: string; teamUrl: string } | null>(null)

  const [deletingPlayer, setDeletingPlayer] = useState<PlayerDto | null>(null)
  const [deletingBusy, setDeletingBusy] = useState(false)

  useEffect(() => {
    load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Each player's game being played right now, if any - shown on their card
  // (with on court / on bench) and kept current while it's live.
  const [liveGames, setLiveGames] = useState<Record<string, GameDto | undefined>>({})
  const loadLiveGames = async (playerIds: string[]) => {
    const entries = await Promise.all(
      playerIds.map(async (playerId): Promise<[string, GameDto | undefined]> => {
        try {
          const { data } = await api.get<GameDto[]>(`/games/player/${playerId}`)
          return [playerId, data.find((g) => g.status === 'In Progress')]
        } catch {
          return [playerId, undefined]
        }
      })
    )
    setLiveGames(Object.fromEntries(entries))
  }
  useEffect(() => {
    if (players.length > 0) loadLiveGames(players.map((p) => p.id))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [players.map((p) => p.id).join(',')])
  useLiveRefresh(Object.values(liveGames).some(Boolean), () => loadLiveGames(players.map((p) => p.id)))

  const load = async () => {
    try {
      setLoading(true)
      if (isPlayerRole && user?.linkedPlayer) {
        setPlayers([user.linkedPlayer])
      } else {
        // (No separate team list anymore - "add an existing team" is gone,
        // teams are created here or linked from IBBA.)
        const { data: playerList } = await api.get<PlayerDto[]>('/players')
        setPlayers(playerList)

        const linkEntries = await Promise.all(
          playerList.map(async (p): Promise<[string, IbbaLinkStatusDto | null]> => {
            try {
              const { data } = await api.get<IbbaLinkStatusDto>(`/players/${p.id}/ibba`)
              return [p.id, data]
            } catch {
              return [p.id, null]
            }
          })
        )
        setIbbaLinks(Object.fromEntries(linkEntries))
      }
      setError(null)
    } catch {
      setError('Could not load players.')
    } finally {
      setLoading(false)
    }
  }

  const addPlayer = async () => {
    if (!form.firstName || !form.lastName) {
      setError('First and last name are required')
      return
    }
    setSaving(true)
    try {
      const { data } = await api.post<PlayerDto>('/players', {
        ...form,
        dateOfBirth: new Date(form.dateOfBirth).toISOString(),
      })
      setPlayers((prev) => [...prev, data])
      setForm(emptyForm)
      setShowAddForm(false)
      setError(null)
    } catch {
      setError('Could not add player.')
    } finally {
      setSaving(false)
    }
  }

  const previewNewIbba = async () => {
    const url = ibbaNewForm.url.trim()
    if (!url) return
    setIbbaNewBusy(true)
    setIbbaNewError(null)
    try {
      const { data } = await api.get<IbbaPreviewDto>('/ibba/preview', { params: { playerUrl: url } })
      setIbbaNewPreview(data)
    } catch (err) {
      const message = (err as { response?: { data?: { message?: string } } })?.response?.data?.message
      setIbbaNewError(message ?? 'Could not read that IBBA page.')
    } finally {
      setIbbaNewBusy(false)
    }
  }

  const createPlayerFromIbba = async () => {
    setIbbaNewBusy(true)
    setIbbaNewError(null)
    try {
      const { data } = await api.post<CreatePlayerFromIbbaResultDto>('/ibba/players', {
        ibbaPlayerUrl: ibbaNewForm.url.trim(),
      })
      setPlayers((prev) => [...prev, data.player])
      setIbbaLinks((prev) => ({ ...prev, [data.player.id]: data.ibba }))
      setIbbaNewForm(emptyIbbaNewForm)
      setIbbaNewPreview(null)
      setShowAddIbbaForm(false)
      setError(null)
      // You already have a team without IBBA - ask whether it's this one.
      if (data.ibba?.teams.some((t) => !t.linkedTeamId)) setChoiceFor(data.player.id)
    } catch {
      setIbbaNewError('Could not create this player from IBBA.')
    } finally {
      setIbbaNewBusy(false)
    }
  }

  const generateInvite = async (playerId: string) => {
    setBusyPlayerId(playerId)
    try {
      const { data } = await api.post<InviteDto>(`/players/${playerId}/invite`, {})
      setInvites((prev) => ({ ...prev, [playerId]: data }))
    } catch {
      setError('Could not generate an invite code.')
    } finally {
      setBusyPlayerId(null)
    }
  }

  const generateParentInvite = async (playerId: string) => {
    setBusyPlayerId(playerId)
    try {
      const { data } = await api.post<InviteDto>(`/players/${playerId}/parent-invite`, {})
      setParentInvites((prev) => ({ ...prev, [playerId]: data }))
    } catch {
      setError('Could not generate a parent invite code.')
    } finally {
      setBusyPlayerId(null)
    }
  }

  const confirmDeletePlayer = async () => {
    if (!deletingPlayer) return
    setDeletingBusy(true)
    try {
      await api.delete(`/players/${deletingPlayer.id}`)
      setPlayers((prev) => prev.filter((p) => p.id !== deletingPlayer.id))
      setDeletingPlayer(null)
    } catch {
      setError('Could not delete this player.')
    } finally {
      setDeletingBusy(false)
    }
  }

  const shareProfile = async (playerId: string) => {
    setBusyPlayerId(playerId)
    try {
      const { data } = await api.post('/share', { playerId })
      const url = `${window.location.origin}/share/${data.token}`
      setShareLinks((prev) => ({ ...prev, [playerId]: url }))
      try {
        await navigator.clipboard.writeText(url)
      } catch {
        // clipboard may be unavailable; the link is still shown below
      }
    } catch {
      setError('Could not create a share link.')
    } finally {
      setBusyPlayerId(null)
    }
  }

  // Re-reads just this one player (teams, photo) and their IBBA link status
  // after an action whose server-side effects reach beyond what the
  // action's own response returns - an IBBA link/sync sets the player's
  // photo, linking an IBBA team creates/attaches a team, and adding or
  // removing a team changes which IBBA team shows as "Synced to".
  const refreshPlayer = async (playerId: string) => {
    const [player, ibba] = await Promise.all([
      api.get<PlayerDto>(`/players/${playerId}`).then((res) => res.data).catch(() => null),
      api.get<IbbaLinkStatusDto>(`/players/${playerId}/ibba`).then((res) => res.data).catch(() => undefined),
    ])
    if (player) setPlayers((prev) => prev.map((p) => (p.id === playerId ? player : p)))
    if (ibba !== undefined) setIbbaLinks((prev) => ({ ...prev, [playerId]: ibba }))
  }

  // After a link or sync the games and standings load in the background -
  // check back until they're in, then show the updated teams (league names
  // added to teams that share a name, etc.).
  const gamesLoadingFor = Object.entries(ibbaLinks).filter(([, link]) => link?.gamesLoading).map(([id]) => id).join(',')
  useEffect(() => {
    if (!gamesLoadingFor) return
    const timer = window.setInterval(() => gamesLoadingFor.split(',').forEach((id) => refreshPlayer(id)), 2500)
    return () => window.clearInterval(timer)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [gamesLoadingFor])

  const attachTeamToPlayer = (playerId: string, team: TeamDto, jerseyNumber?: number) => {
    setPlayers((prev) =>
      prev.map((p) =>
        p.id === playerId && !(p.teams ?? []).some((t) => t.id === team.id)
          ? { ...p, teams: [...(p.teams ?? []), { ...team, jerseyNumber: jerseyNumber ?? team.jerseyNumber ?? 0 }] }
          : p
      )
    )
  }

  const createAndAddTeam = async (playerId: string) => {
    if (!newTeamName.trim()) return
    const jerseyNumber = pickerJerseyNumber.trim() === '' ? undefined : Number(pickerJerseyNumber)
    setTeamBusy(true)
    try {
      const { data: team } = await api.post<TeamDto>('/teams', { name: newTeamName.trim() })
      await api.post(`/teams/${team.id}/players/${playerId}`, { jerseyNumber })
      attachTeamToPlayer(playerId, team, jerseyNumber)
      refreshPlayer(playerId)
      setNewTeamName('')
      setPickerJerseyNumber('')
      setTeamPickerOpenFor(null)
    } catch {
      setError('Could not create the team.')
    } finally {
      setTeamBusy(false)
    }
  }

  const removeTeam = async (playerId: string, teamId: string) => {
    try {
      await api.delete(`/teams/${teamId}/players/${playerId}`)
      setPlayers((prev) => prev.map((p) => (p.id === playerId ? { ...p, teams: (p.teams ?? []).filter((t) => t.id !== teamId) } : p)))
      refreshPlayer(playerId)
    } catch {
      setError('Could not remove player from that team.')
    }
  }

  const commitRename = async () => {
    if (!renaming) return
    const name = renaming.value.trim()
    const current = players.find((p) => p.id === renaming.playerId)?.teams?.find((t) => t.id === renaming.teamId)?.name
    if (!name || name === current) {
      setRenaming(null)
      return
    }
    setRenaming({ ...renaming, busy: true })
    try {
      const { data } = await api.put<TeamDto>(`/teams/${renaming.teamId}`, { name })
      const teamId = renaming.teamId
      // The same team can be on more than one player's card (siblings) and
      // behind IBBA link info - rename it everywhere it's shown.
      setPlayers((prev) =>
        prev.map((p) => ({ ...p, teams: (p.teams ?? []).map((t) => (t.id === teamId ? { ...t, name: data.name } : t)) }))
      )
      setIbbaLinks((prev) =>
        Object.fromEntries(
          Object.entries(prev).map(([pid, link]) => [
            pid,
            link && { ...link, teams: link.teams.map((it) => (it.linkedTeamId === teamId ? { ...it, linkedTeamName: data.name } : it)) },
          ])
        )
      )
      setRenaming(null)
    } catch {
      setError('Could not rename that team.')
      setRenaming((prev) => (prev ? { ...prev, busy: false } : prev))
    }
  }

  const editJerseyKey = (playerId: string, teamId: string) => `${playerId}-${teamId}`

  const commitJerseyEdit = async (playerId: string, teamId: string) => {
    const key = editJerseyKey(playerId, teamId)
    const raw = jerseyEdits[key]
    if (raw === undefined) return
    const jerseyNumber = Number(raw)
    setJerseyEdits((prev) => {
      const next = { ...prev }
      delete next[key]
      return next
    })
    if (Number.isNaN(jerseyNumber)) return
    try {
      await api.put(`/teams/${teamId}/players/${playerId}`, { jerseyNumber })
      setPlayers((prev) =>
        prev.map((p) =>
          p.id === playerId ? { ...p, teams: (p.teams ?? []).map((t) => (t.id === teamId ? { ...t, jerseyNumber } : t)) } : p
        )
      )
    } catch {
      setError("Could not update that team's jersey number.")
    }
  }

  const previewIbba = async (playerId: string) => {
    const url = (ibbaUrlInput[playerId] ?? '').trim()
    if (!url) return
    setIbbaBusy((prev) => ({ ...prev, [playerId]: true }))
    setIbbaError((prev) => ({ ...prev, [playerId]: null }))
    try {
      const { data } = await api.get<IbbaPreviewDto>('/ibba/preview', { params: { playerUrl: url } })
      setIbbaPreview((prev) => ({ ...prev, [playerId]: data }))
    } catch (err) {
      const message = (err as { response?: { data?: { message?: string } } })?.response?.data?.message
      setIbbaError((prev) => ({ ...prev, [playerId]: message ?? 'Could not read that IBBA page.' }))
    } finally {
      setIbbaBusy((prev) => ({ ...prev, [playerId]: false }))
    }
  }

  const confirmLinkIbba = async (playerId: string) => {
    const url = (ibbaUrlInput[playerId] ?? '').trim()
    if (!url) return
    setIbbaBusy((prev) => ({ ...prev, [playerId]: true }))
    try {
      const { data } = await api.post<IbbaLinkStatusDto>(`/players/${playerId}/ibba/link`, { ibbaPlayerUrl: url })
      setIbbaLinks((prev) => ({ ...prev, [playerId]: data }))
      // The link's first sync sets the IBBA photo and creates/links the
      // player's teams - show them, and ask about any team that needs a choice.
      await refreshPlayer(playerId)
      if (data.teams.some((t) => !t.linkedTeamId)) setChoiceFor(playerId)
      setIbbaPreview((prev) => ({ ...prev, [playerId]: null }))
      setIbbaUrlInput((prev) => ({ ...prev, [playerId]: '' }))
      setIbbaError((prev) => ({ ...prev, [playerId]: null }))
    } catch {
      setIbbaError((prev) => ({ ...prev, [playerId]: 'Could not link this player to IBBA.' }))
    } finally {
      setIbbaBusy((prev) => ({ ...prev, [playerId]: false }))
    }
  }

  const syncIbba = async (playerId: string) => {
    setIbbaBusy((prev) => ({ ...prev, [playerId]: true }))
    try {
      const { data } = await api.post<IbbaLinkStatusDto>(`/players/${playerId}/ibba/sync`)
      setIbbaLinks((prev) => ({ ...prev, [playerId]: data }))
      // A sync can update the IBBA photo and link a newly joined IBBA team.
      await refreshPlayer(playerId)
      if (data.teams.some((t) => !t.linkedTeamId)) setChoiceFor(playerId)
    } catch {
      setIbbaError((prev) => ({ ...prev, [playerId]: 'Sync failed - try again shortly.' }))
    } finally {
      setIbbaBusy((prev) => ({ ...prev, [playerId]: false }))
    }
  }

  const unlinkIbba = async (playerId: string) => {
    if (!window.confirm('Disconnect this player from IBBA? Games already synced keep their stats - only the IBBA connection itself is removed.')) return
    setIbbaBusy((prev) => ({ ...prev, [playerId]: true }))
    try {
      await api.delete(`/players/${playerId}/ibba/link`)
      setIbbaLinks((prev) => ({ ...prev, [playerId]: null }))
    } catch {
      setIbbaError((prev) => ({ ...prev, [playerId]: 'Could not disconnect - try again shortly.' }))
    } finally {
      setIbbaBusy((prev) => ({ ...prev, [playerId]: false }))
    }
  }

  const mapIbbaTeamToExisting = async (playerId: string, ibbaTeamLinkId: string, teamId: string) => {
    setIbbaBusy((prev) => ({ ...prev, [playerId]: true }))
    try {
      const { data } = await api.put<IbbaLinkStatusDto>(`/ibba/team-links/${ibbaTeamLinkId}`, { teamId, playerId })
      setIbbaLinks((prev) => ({ ...prev, [playerId]: data }))
      await refreshPlayer(playerId)
    } catch {
      setIbbaError((prev) => ({ ...prev, [playerId]: 'Could not link that team.' }))
    } finally {
      setIbbaBusy((prev) => ({ ...prev, [playerId]: false }))
    }
  }

  // "Create a new team" in the choice pop-up - the server names it (adding
  // the league when the name repeats), links it, and syncs its games.
  const createTeamForIbba = async (playerId: string, ibbaTeamId: string) => {
    setIbbaBusy((prev) => ({ ...prev, [playerId]: true }))
    try {
      const { data } = await api.post<IbbaLinkStatusDto>(`/ibba/team-links/${ibbaTeamId}/new-team`, { playerId })
      setIbbaLinks((prev) => ({ ...prev, [playerId]: data }))
    } catch {
      setIbbaError((prev) => ({ ...prev, [playerId]: 'Could not create that team.' }))
    } finally {
      await refreshPlayer(playerId)
      setIbbaBusy((prev) => ({ ...prev, [playerId]: false }))
    }
  }

  if (loading) {
    return (
      <div className="page-container">
        <h2>👤 Player Profiles</h2>
        <BouncingBall />
      </div>
    )
  }

  return (
    <div className="page-container">
      <h2>👤 {isPlayerRole ? 'My Profile' : 'Player Profiles'}</h2>
      {error && <p className="error">{error}</p>}

      <div className="profiles-grid">
        {players.map((player) => (
          <div className="profile-card profile-card-v2" key={player.id}>
            <div className="profile-top-row">
              {player.profilePictureUrl ? (
                <img className="avatar-lg" src={player.profilePictureUrl} alt="" />
              ) : (
                <div className="avatar-lg">{player.firstName[0]}{player.lastName[0]}</div>
              )}
              <div className="profile-id">
                <h3>{player.firstName} {player.lastName}</h3>
                <p className="profile-id-sub">{player.position || 'Player'}</p>
                {liveGames[player.id] && (() => {
                  const game = liveGames[player.id]!
                  const onCourt = onCourtIn(game, player.id)
                  return (
                    <Link className="profile-live-line" to={`/games/${game.id}?playerId=${player.id}`}>
                      <span className="live-title-tag"><span className="fab-live-dot" /> Live</span>
                      <span className="profile-live-opponent" dir="auto">vs {game.opponentName}</span>
                      {onCourt != null && <OnCourtChip onCourt={onCourt} />}
                    </Link>
                  )
                })()}
              </div>
            </div>

            <div className="profile-info">
              <p>📅 Born: {new Date(player.dateOfBirth).toLocaleDateString()}</p>
              {player.height && <p>📏 Height: {player.height} cm</p>}
              {player.weight && <p>⚖️ Weight: {player.weight} kg</p>}
            </div>

            <div className="team-section">
              <div className="team-chip-row" style={{ flexDirection: 'column', alignItems: 'stretch' }}>
                {(player.teams ?? []).map((t) => {
                  const ibbaTeam = ibbaLinks[player.id]?.teams.find((it) => it.linkedTeamId === t.id)
                  return (
                    <div className="team-chip-v2" key={t.id}>
                      <TeamCrest
                        logoUrl={ibbaTeam?.teamLogoUrl}
                        jerseyNumber={t.jerseyNumber}
                        showIbbaMark={!!ibbaTeam}
                        size="sm"
                        onClick={ibbaTeam?.ibbaLeagueUrl ? () => setStandingsFor({ leagueUrl: ibbaTeam.ibbaLeagueUrl!, leagueName: ibbaTeam.ibbaLeagueName ?? '', teamName: t.name, teamUrl: ibbaTeam.teamUrl }) : undefined}
                        title={ibbaTeam?.ibbaLeagueUrl ? 'View standings' : undefined}
                      />
                      <div className="tcv2-info">
                        {renaming?.playerId === player.id && renaming.teamId === t.id ? (
                          <form
                            className="tcv2-rename"
                            onSubmit={(e) => {
                              e.preventDefault()
                              commitRename()
                            }}
                          >
                            <input
                              autoFocus
                              aria-label="Team name"
                              value={renaming.value}
                              maxLength={100}
                              disabled={renaming.busy}
                              onChange={(e) => setRenaming({ ...renaming, value: e.target.value })}
                              onKeyDown={(e) => {
                                if (e.key === 'Escape') setRenaming(null)
                              }}
                            />
                            <button type="submit" className="tcv2-rename-btn save" disabled={renaming.busy || !renaming.value.trim()} title="Save name">
                              <svg className="icon"><use href="#i-check" /></svg>
                            </button>
                            <button type="button" className="tcv2-rename-btn" onClick={() => setRenaming(null)} disabled={renaming.busy} title="Cancel">
                              <svg className="icon"><use href="#i-x" /></svg>
                            </button>
                          </form>
                        ) : (
                          <span className="tcv2-name-row">
                            <span className="tcv2-name team-name-clamp" title={t.name}>{t.name}</span>
                            {!isPlayerRole && (
                              <button
                                className="tcv2-edit-name"
                                onClick={() => setRenaming({ playerId: player.id, teamId: t.id, value: t.name, busy: false })}
                                title="Rename team"
                                aria-label={`Rename ${t.name}`}
                              >
                                <svg className="icon"><use href="#i-edit" /></svg>
                              </button>
                            )}
                          </span>
                        )}
                        {ibbaTeam?.ibbaLeagueName && (
                          <button
                            className="league-chip tcv2-league"
                            onClick={() => setStandingsFor({ leagueUrl: ibbaTeam.ibbaLeagueUrl!, leagueName: ibbaTeam.ibbaLeagueName ?? '', teamName: t.name, teamUrl: ibbaTeam.teamUrl })}
                            title="View standings"
                          >
                            <svg className="icon"><use href="#i-trophy" /></svg>
                            <span dir="rtl">{ibbaTeam.ibbaLeagueName}</span>
                            {!!ibbaTeam.position && ibbaTeam.position > 0 && ` · ${ibbaTeam.position} of ${ibbaTeam.totalTeams}`}
                          </button>
                        )}
                        {!isPlayerRole ? (
                          <div className="tcv2-jersey-row">
                            <label htmlFor={`jersey-${player.id}-${t.id}`}>Jersey</label>
                            <input
                              id={`jersey-${player.id}-${t.id}`}
                              type="number"
                              className="team-chip-jersey"
                              title={`Jersey number on ${t.name}`}
                              value={jerseyEdits[editJerseyKey(player.id, t.id)] ?? String(t.jerseyNumber ?? 0)}
                              onChange={(e) =>
                                setJerseyEdits((prev) => ({ ...prev, [editJerseyKey(player.id, t.id)]: e.target.value }))
                              }
                              onBlur={() => commitJerseyEdit(player.id, t.id)}
                            />
                          </div>
                        ) : (
                          <span className="player-card-number">#{t.jerseyNumber}</span>
                        )}
                      </div>
                      {!isPlayerRole && (
                        <button className="team-chip-remove" onClick={() => removeTeam(player.id, t.id)} title="Remove from team">
                          ×
                        </button>
                      )}
                    </div>
                  )
                })}
                {(player.teams ?? []).length === 0 && <span className="team-chip-empty">No team yet</span>}
              </div>

              {!isPlayerRole && (
                <>
                  {teamPickerOpenFor === player.id ? (
                    <div className="team-picker">
                      {/* Name on its own full-width line - squeezed into one row with
                          the jersey box and buttons, it was ~70px wide on a phone and
                          you couldn't see what you typed. */}
                      <input
                        type="text"
                        className="team-picker-name"
                        placeholder="New team name (e.g. U16)"
                        aria-label="New team name"
                        value={newTeamName}
                        autoFocus
                        maxLength={100}
                        onChange={(e) => setNewTeamName(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter') createAndAddTeam(player.id)
                        }}
                      />
                      <div className="team-picker-row">
                        <input
                          type="number"
                          className="team-picker-jersey"
                          placeholder="Jersey #"
                          aria-label="Jersey number"
                          value={pickerJerseyNumber}
                          onChange={(e) => setPickerJerseyNumber(e.target.value)}
                        />
                        <button className="submit-btn" disabled={teamBusy || !newTeamName.trim()} onClick={() => createAndAddTeam(player.id)}>
                          Add
                        </button>
                        <button className="nav-btn" onClick={() => { setTeamPickerOpenFor(null); setNewTeamName(''); setPickerJerseyNumber('') }}>
                          Cancel
                        </button>
                      </div>
                    </div>
                  ) : (
                    <button className="add-team-btn" onClick={() => setTeamPickerOpenFor(player.id)}>+ Team</button>
                  )}
                </>
              )}
            </div>

            <div className="ibba-connect-box">
              {ibbaLinks[player.id] ? (
                <>
                  <p className="profile-section-label" style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                    <IbbaBadge />
                    {ibbaLinks[player.id]!.lastSyncedAt && (
                      <span style={{ textTransform: 'none', letterSpacing: 0, fontWeight: 400 }}>
                        Last synced {new Date(ibbaLinks[player.id]!.lastSyncedAt!).toLocaleString()}
                      </span>
                    )}
                  </p>
                  {ibbaLinks[player.id]!.gamesLoading && (
                    <BouncingBall size="sm" label="Loading games and standings…" />
                  )}
                  {ibbaLinks[player.id]!.lastSyncError && (
                    <p className="error">{ibbaLinks[player.id]!.lastSyncError}</p>
                  )}
                  {/* The player's IBBA teams show once, in the team list above (with
                      their league and standings) - here only what needs a decision. */}
                  {(() => {
                    const pending = ibbaLinks[player.id]!.teams.filter((t) => !t.linkedTeamId).length
                    return pending > 0 ? (
                      <div className="ibba-pending-row">
                        <span>{pending === 1 ? '1 IBBA team needs your choice' : `${pending} IBBA teams need your choice`}</span>
                        <button className="submit-btn" onClick={() => setChoiceFor(player.id)}>Choose</button>
                      </div>
                    ) : null
                  })()}
                  <div className="flex gap-1" style={{ marginTop: '0.75rem' }}>
                    <button className="add-team-btn" disabled={ibbaBusy[player.id]} onClick={() => syncIbba(player.id)}>
                      {ibbaBusy[player.id] ? <Busy>Syncing…</Busy> : '🔄 Sync Now'}
                    </button>
                    <button className="add-team-btn" disabled={ibbaBusy[player.id]} onClick={() => unlinkIbba(player.id)}>
                      Disconnect from IBBA
                    </button>
                  </div>
                </>
              ) : (
                <>
                  <p className="profile-section-label">Connect to IBBA</p>
                  {ibbaPreview[player.id] ? (
                    <div className="invite-box">
                      <p>
                        Found: <b>{ibbaPreview[player.id]!.playerName}</b>
                        {ibbaPreview[player.id]!.teams.length > 0 && (
                          <> · {ibbaPreview[player.id]!.teams.map((t) => t.teamName).join(', ')}</>
                        )}
                      </p>
                      <div className="flex gap-1" style={{ marginTop: '0.5rem' }}>
                        <button className="submit-btn" disabled={ibbaBusy[player.id]} onClick={() => confirmLinkIbba(player.id)}>
                          Confirm Link
                        </button>
                        <button className="nav-btn" onClick={() => setIbbaPreview((prev) => ({ ...prev, [player.id]: null }))}>
                          Cancel
                        </button>
                      </div>
                    </div>
                  ) : (
                    <div className="ibba-connect-form">
                      <input
                        type="text"
                        placeholder="Paste this player's ibasketball.co.il profile URL"
                        value={ibbaUrlInput[player.id] ?? ''}
                        onChange={(e) => setIbbaUrlInput((prev) => ({ ...prev, [player.id]: e.target.value }))}
                      />
                      <button className="submit-btn" disabled={ibbaBusy[player.id] || !(ibbaUrlInput[player.id] ?? '').trim()} onClick={() => previewIbba(player.id)}>
                        {ibbaBusy[player.id] ? <Busy>Checking…</Busy> : 'Preview'}
                      </button>
                    </div>
                  )}
                  {ibbaError[player.id] && <p className="error">{ibbaError[player.id]}</p>}
                </>
              )}
            </div>

            <div className="team-section">
              <p className="profile-section-label">Connected Parents</p>
              <div className="parent-row">
                {(player.parents ?? []).map((p) => (
                  <span className="parent-chip" key={p.userId}>
                    <span className="parent-avatar">{p.firstName[0]}</span>
                    {p.firstName}
                    {p.userId === user?.id && <span className="you-tag">You</span>}
                  </span>
                ))}
                {!isPlayerRole && (
                  <button onClick={() => generateParentInvite(player.id)} disabled={busyPlayerId === player.id}>
                    + Invite Parent
                  </button>
                )}
              </div>
              {parentInvites[player.id] && (
                <div className="invite-box">
                  <p>Give this code to the other parent. They sign in, then enter it as "Join as a second parent":</p>
                  <code className="invite-code">{parentInvites[player.id].inviteCode}</code>
                  <p className="invite-expiry">Expires {new Date(parentInvites[player.id].expiresAt).toLocaleDateString()}</p>
                </div>
              )}
            </div>

            <div className="profile-actions">
              <button onClick={() => shareProfile(player.id)} disabled={busyPlayerId === player.id}>
                🔗 Share Stats
              </button>
              {!isPlayerRole && (
                <button onClick={() => generateInvite(player.id)} disabled={busyPlayerId === player.id}>
                  🔑 Player Login Code
                </button>
              )}
              {!isPlayerRole && (
                <button onClick={() => setDeletingPlayer(player)} style={{ color: 'var(--color-danger)' }}>
                  🗑️ Delete Player
                </button>
              )}
            </div>
            {shareLinks[player.id] && (
              <div className="invite-box">
                <p>Share link (copied to clipboard):</p>
                <code>{shareLinks[player.id]}</code>
              </div>
            )}
            {invites[player.id] && (
              <div className="invite-box">
                <p>Give this code to your player. They sign in, then enter it on the "Join" page:</p>
                <code className="invite-code">{invites[player.id].inviteCode}</code>
                <p className="invite-expiry">Expires {new Date(invites[player.id].expiresAt).toLocaleDateString()}</p>
              </div>
            )}
          </div>
        ))}

        {!isPlayerRole && !showAddForm && (
          <div className="add-player-card">
            {showAddIbbaForm ? (
              ibbaNewPreview ? (
                <>
                  <h3>➕ New Player from IBBA</h3>
                  <p>
                    Found: <b>{ibbaNewPreview.playerName}</b>
                    {ibbaNewPreview.dateOfBirth && <> · born {new Date(ibbaNewPreview.dateOfBirth).toLocaleDateString()}</>}
                    {ibbaNewPreview.teams.length > 0 && <> · {ibbaNewPreview.teams.map((t) => t.teamName).join(', ')}</>}
                  </p>
                  {!ibbaNewPreview.dateOfBirth && (
                    <p className="error">This IBBA profile doesn't list a birth date - add this player manually instead.</p>
                  )}
                  {ibbaNewError && <p className="error">{ibbaNewError}</p>}
                  <div className="flex gap-1" style={{ justifyContent: 'center', flexWrap: 'wrap' }}>
                    {ibbaNewPreview.dateOfBirth && (
                      <button className="submit-btn" onClick={createPlayerFromIbba} disabled={ibbaNewBusy}>
                        {ibbaNewBusy ? <Busy>Creating…</Busy> : 'Create Player'}
                      </button>
                    )}
                    <button onClick={() => setIbbaNewPreview(null)} disabled={ibbaNewBusy}>Back</button>
                    <button
                      onClick={() => { setShowAddIbbaForm(false); setIbbaNewPreview(null); setIbbaNewForm(emptyIbbaNewForm); setIbbaNewError(null) }}
                    >
                      Cancel
                    </button>
                  </div>
                </>
              ) : (
                <>
                  <h3>➕ New Player from IBBA</h3>
                  <div className="ibba-connect-form">
                    <input
                      type="text"
                      placeholder="Paste this player's ibasketball.co.il profile URL"
                      value={ibbaNewForm.url}
                      onChange={(e) => setIbbaNewForm({ ...ibbaNewForm, url: e.target.value })}
                    />
                    <button className="submit-btn" disabled={ibbaNewBusy || !ibbaNewForm.url.trim()} onClick={previewNewIbba}>
                      {ibbaNewBusy ? <Busy>Checking…</Busy> : 'Preview'}
                    </button>
                  </div>
                  {ibbaNewError && <p className="error">{ibbaNewError}</p>}
                  <button onClick={() => setShowAddIbbaForm(false)}>Cancel</button>
                </>
              )
            ) : (
              <>
                <h3>➕ Add New Player</h3>
                <p>Manage multiple players</p>
                <div className="flex gap-1" style={{ justifyContent: 'center', flexWrap: 'wrap' }}>
                  <button onClick={() => setShowAddForm(true)}>Add Manually</button>
                  <button onClick={() => setShowAddIbbaForm(true)}>Add via IBBA Link</button>
                </div>
              </>
            )}
          </div>
        )}
      </div>

      {!isPlayerRole && showAddForm && (
        <div className="form-section" style={{ marginTop: '2rem' }}>
          <h3>New Player</h3>
          <div className="form-row">
            <label>
              First Name
              <input value={form.firstName} onChange={(e) => setForm({ ...form, firstName: e.target.value })} />
            </label>
            <label>
              Last Name
              <input value={form.lastName} onChange={(e) => setForm({ ...form, lastName: e.target.value })} />
            </label>
            <label>
              Position
              <input value={form.position} onChange={(e) => setForm({ ...form, position: e.target.value })} placeholder="PG, SG, SF, PF, C" />
            </label>
            <label>
              Date of Birth
              <input type="date" value={form.dateOfBirth} onChange={(e) => setForm({ ...form, dateOfBirth: e.target.value })} />
            </label>
          </div>
          <div className="flex gap-1">
            <button className="submit-btn" onClick={addPlayer} disabled={saving}>
              {saving ? <Busy>Saving…</Busy> : 'Save Player'}
            </button>
            <button className="nav-btn" onClick={() => setShowAddForm(false)}>Cancel</button>
          </div>
        </div>
      )}

      {choiceFor !== null && (() => {
        const player = players.find((p) => p.id === choiceFor)
        const link = ibbaLinks[choiceFor]
        const pending = link?.teams.filter((t) => !t.linkedTeamId) ?? []
        if (!player || pending.length === 0) return null
        // Your teams that aren't linked to any IBBA team yet.
        const candidates = link?.existingTeams ?? []
        return (
          <IbbaTeamChoiceModal
            playerName={player.firstName}
            pending={pending}
            total={link?.teams.length ?? pending.length}
            candidates={candidates}
            busy={!!ibbaBusy[player.id]}
            onLink={(ibbaTeamId, teamId) => mapIbbaTeamToExisting(player.id, ibbaTeamId, teamId)}
            onCreate={(ibbaTeamId) => createTeamForIbba(player.id, ibbaTeamId)}
            onClose={() => setChoiceFor(null)}
          />
        )
      })()}

      {standingsFor && (
        <StandingsModal
          leagueUrl={standingsFor.leagueUrl}
          leagueName={standingsFor.leagueName}
          highlightTeamUrl={standingsFor.teamUrl}
          highlightTeamName={standingsFor.teamName}
          onClose={() => setStandingsFor(null)}
        />
      )}

      {deletingPlayer && (
        <ConfirmModal
          title="Delete Player"
          message={
            <>
              Delete <b>{deletingPlayer.firstName} {deletingPlayer.lastName}</b>? This removes every team, game, and
              stat that belongs only to them - a team shared with a sibling is kept, only their own membership and
              stats on it go away. This can't be undone.
            </>
          }
          confirmLabel="Delete Player"
          busy={deletingBusy}
          onConfirm={confirmDeletePlayer}
          onCancel={() => setDeletingPlayer(null)}
        />
      )}
    </div>
  )
}

// "Link to your existing team, or create a new one?" for each IBBA team that
// couldn't be linked automatically - only asked when the player already has a
// team that isn't linked to IBBA (it might be the same team).
function IbbaTeamChoiceModal({
  playerName,
  pending,
  total,
  candidates,
  busy,
  onLink,
  onCreate,
  onClose,
}: {
  playerName: string
  pending: IbbaLinkStatusDto['teams']
  total: number
  candidates: TeamDto[]
  busy: boolean
  onLink: (ibbaTeamId: string, teamId: string) => void
  onCreate: (ibbaTeamId: string) => void
  onClose: () => void
}) {
  const [picked, setPicked] = useState<Record<string, string>>({})

  return (
    <div className="game-edit-modal-backdrop" onClick={onClose}>
      <div className="game-edit-modal-panel ibba-choice-panel" onClick={(e) => e.stopPropagation()} role="dialog" aria-label="Link IBBA teams">
        <div className="modal-head">
          <div className="modal-head-title">
            <h3>Add {playerName}&apos;s IBBA team to your team?</h3>
            {total > 1 && <p className="ibba-choice-step">IBBA team {total - pending.length + 1} of {total}</p>}
            <p>
              {candidates.length > 0
                ? `Pick the existing team this IBBA team is - its games go there. A new team is made only if you say no.`
                : `All your teams are linked to IBBA now - create a team for this one.`}
            </p>
          </div>
          <button className="modal-close" onClick={onClose} aria-label="Close">
            <svg className="icon"><use href="#i-x" /></svg>
          </button>
        </div>
        <div className="modal-body ibba-choice-list">
          {busy && (
            <p className="ibba-choice-busy"><Busy>Linking the team…</Busy></p>
          )}
          {pending.slice(0, 1).map((t) => {
            const teamId = picked[t.id] ?? candidates[0]?.id
            return (
              <div className="ibba-choice-item" key={t.id}>
                <div className="ibba-choice-team">
                  <TeamCrest logoUrl={t.teamLogoUrl} showIbbaMark size="sm" />
                  <div style={{ minWidth: 0 }}>
                    <div className="team-name-clamp ibba-choice-name" dir="auto" title={t.teamName}>{t.teamName}</div>
                    {t.ibbaLeagueName && <div className="ibba-choice-league" dir="rtl">{t.ibbaLeagueName}</div>}
                  </div>
                </div>
                {candidates.length > 0 && (
                  <div className="ibba-choice-option">
                    <select value={teamId} onChange={(e) => setPicked((prev) => ({ ...prev, [t.id]: e.target.value }))} disabled={busy}>
                      {candidates.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                    </select>
                    <button className="submit-btn" disabled={busy || !teamId} onClick={() => teamId && onLink(t.id, teamId)}>
                      Yes, add to this team
                    </button>
                  </div>
                )}
                <button className="add-team-btn ibba-choice-new" disabled={busy} onClick={() => onCreate(t.id)}>
                  {candidates.length > 0 ? 'No, create a new team' : 'Create a new team'}
                </button>
              </div>
            )
          })}
        </div>
      </div>
    </div>
  )
}
