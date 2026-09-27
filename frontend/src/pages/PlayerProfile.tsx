import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { CreatePlayerFromIbbaResultDto, IbbaLinkStatusDto, IbbaPreviewDto, InviteDto, PlayerDto, TeamDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import ConfirmModal from '../components/ConfirmModal'
import IbbaBadge from '../components/IbbaBadge'
import StandingsModal from '../components/StandingsModal'
import TeamCrest from '../components/TeamCrest'

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
  const [allTeams, setAllTeams] = useState<TeamDto[]>([])
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

  const [invites, setInvites] = useState<Record<number, InviteDto>>({})
  const [parentInvites, setParentInvites] = useState<Record<number, InviteDto>>({})
  const [shareLinks, setShareLinks] = useState<Record<number, string>>({})
  const [busyPlayerId, setBusyPlayerId] = useState<number | null>(null)

  const [teamPickerOpenFor, setTeamPickerOpenFor] = useState<number | null>(null)
  const [newTeamName, setNewTeamName] = useState('')
  const [pickerJerseyNumber, setPickerJerseyNumber] = useState('')
  const [teamBusy, setTeamBusy] = useState(false)
  const [jerseyEdits, setJerseyEdits] = useState<Record<string, string>>({})
  const [renaming, setRenaming] = useState<{ playerId: number; teamId: number; value: string; busy: boolean } | null>(null)

  const [ibbaLinks, setIbbaLinks] = useState<Record<number, IbbaLinkStatusDto | null>>({})
  const [ibbaUrlInput, setIbbaUrlInput] = useState<Record<number, string>>({})
  const [ibbaPreview, setIbbaPreview] = useState<Record<number, IbbaPreviewDto | null>>({})
  const [ibbaBusy, setIbbaBusy] = useState<Record<number, boolean>>({})
  const [ibbaError, setIbbaError] = useState<Record<number, string | null>>({})
  const [standingsFor, setStandingsFor] = useState<{ leagueUrl: string; leagueName: string; teamName: string; teamUrl: string } | null>(null)

  const [deletingPlayer, setDeletingPlayer] = useState<PlayerDto | null>(null)
  const [deletingBusy, setDeletingBusy] = useState(false)

  useEffect(() => {
    load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const load = async () => {
    try {
      setLoading(true)
      if (isPlayerRole && user?.linkedPlayer) {
        setPlayers([user.linkedPlayer])
      } else {
        const [{ data: playerList }, { data: teamList }] = await Promise.all([
          api.get<PlayerDto[]>('/players'),
          api.get<TeamDto[]>('/teams'),
        ])
        setPlayers(playerList)
        setAllTeams(teamList)

        const linkEntries = await Promise.all(
          playerList.map(async (p): Promise<[number, IbbaLinkStatusDto | null]> => {
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
    } catch {
      setIbbaNewError('Could not create this player from IBBA.')
    } finally {
      setIbbaNewBusy(false)
    }
  }

  const generateInvite = async (playerId: number) => {
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

  const generateParentInvite = async (playerId: number) => {
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

  const shareProfile = async (playerId: number) => {
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
  const refreshPlayer = async (playerId: number) => {
    const [player, ibba] = await Promise.all([
      api.get<PlayerDto>(`/players/${playerId}`).then((res) => res.data).catch(() => null),
      api.get<IbbaLinkStatusDto>(`/players/${playerId}/ibba`).then((res) => res.data).catch(() => undefined),
    ])
    if (player) setPlayers((prev) => prev.map((p) => (p.id === playerId ? player : p)))
    if (ibba !== undefined) setIbbaLinks((prev) => ({ ...prev, [playerId]: ibba }))
  }

  const attachTeamToPlayer = (playerId: number, team: TeamDto, jerseyNumber?: number) => {
    setPlayers((prev) =>
      prev.map((p) =>
        p.id === playerId && !(p.teams ?? []).some((t) => t.id === team.id)
          ? { ...p, teams: [...(p.teams ?? []), { ...team, jerseyNumber: jerseyNumber ?? team.jerseyNumber ?? 0 }] }
          : p
      )
    )
  }

  const addExistingTeam = async (playerId: number, teamId: number) => {
    const team = allTeams.find((t) => t.id === teamId)
    if (!team) return
    const jerseyNumber = pickerJerseyNumber.trim() === '' ? undefined : Number(pickerJerseyNumber)
    setTeamBusy(true)
    try {
      await api.post(`/teams/${teamId}/players/${playerId}`, { jerseyNumber })
      attachTeamToPlayer(playerId, team, jerseyNumber)
      refreshPlayer(playerId)
      setTeamPickerOpenFor(null)
      setPickerJerseyNumber('')
    } catch {
      setError('Could not add player to that team.')
    } finally {
      setTeamBusy(false)
    }
  }

  const createAndAddTeam = async (playerId: number) => {
    if (!newTeamName.trim()) return
    const jerseyNumber = pickerJerseyNumber.trim() === '' ? undefined : Number(pickerJerseyNumber)
    setTeamBusy(true)
    try {
      const { data: team } = await api.post<TeamDto>('/teams', { name: newTeamName.trim() })
      await api.post(`/teams/${team.id}/players/${playerId}`, { jerseyNumber })
      setAllTeams((prev) => [...prev, team])
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

  const removeTeam = async (playerId: number, teamId: number) => {
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
      // The same team can be on more than one player's card (siblings), in
      // the "add existing team" picker, and in an IBBA "Synced to" label -
      // rename it everywhere it's shown.
      setPlayers((prev) =>
        prev.map((p) => ({ ...p, teams: (p.teams ?? []).map((t) => (t.id === teamId ? { ...t, name: data.name } : t)) }))
      )
      setAllTeams((prev) => prev.map((t) => (t.id === teamId ? { ...t, name: data.name } : t)))
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

  const editJerseyKey = (playerId: number, teamId: number) => `${playerId}-${teamId}`

  const commitJerseyEdit = async (playerId: number, teamId: number) => {
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

  const previewIbba = async (playerId: number) => {
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

  const confirmLinkIbba = async (playerId: number) => {
    const url = (ibbaUrlInput[playerId] ?? '').trim()
    if (!url) return
    setIbbaBusy((prev) => ({ ...prev, [playerId]: true }))
    try {
      const { data } = await api.post<IbbaLinkStatusDto>(`/players/${playerId}/ibba/link`, { ibbaPlayerUrl: url })
      setIbbaLinks((prev) => ({ ...prev, [playerId]: data }))
      refreshPlayer(playerId) // the link's first sync sets the IBBA profile photo
      setIbbaPreview((prev) => ({ ...prev, [playerId]: null }))
      setIbbaUrlInput((prev) => ({ ...prev, [playerId]: '' }))
      setIbbaError((prev) => ({ ...prev, [playerId]: null }))
    } catch {
      setIbbaError((prev) => ({ ...prev, [playerId]: 'Could not link this player to IBBA.' }))
    } finally {
      setIbbaBusy((prev) => ({ ...prev, [playerId]: false }))
    }
  }

  const syncIbba = async (playerId: number) => {
    setIbbaBusy((prev) => ({ ...prev, [playerId]: true }))
    try {
      const { data } = await api.post<IbbaLinkStatusDto>(`/players/${playerId}/ibba/sync`)
      setIbbaLinks((prev) => ({ ...prev, [playerId]: data }))
      refreshPlayer(playerId) // a sync can update the IBBA profile photo
    } catch {
      setIbbaError((prev) => ({ ...prev, [playerId]: 'Sync failed - try again shortly.' }))
    } finally {
      setIbbaBusy((prev) => ({ ...prev, [playerId]: false }))
    }
  }

  const unlinkIbba = async (playerId: number) => {
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

  const mapIbbaTeamToExisting = async (playerId: number, ibbaTeamLinkId: number, teamId: number) => {
    setIbbaBusy((prev) => ({ ...prev, [playerId]: true }))
    try {
      const { data } = await api.put<IbbaLinkStatusDto>(`/ibba/team-links/${ibbaTeamLinkId}`, { teamId })
      setIbbaLinks((prev) => ({ ...prev, [playerId]: data }))
      refreshPlayer(playerId)
    } catch {
      setIbbaError((prev) => ({ ...prev, [playerId]: 'Could not link that team.' }))
    } finally {
      setIbbaBusy((prev) => ({ ...prev, [playerId]: false }))
    }
  }

  // A player on two IBBA teams that share a name (the same club in two
  // leagues/age groups, e.g. both "מכבי תל מונד") would otherwise end up with
  // two identically-named teams everywhere in the app. When the name isn't
  // unique for this player - among their IBBA teams or teams they're already
  // on - add the league to tell them apart.
  const nameForNewIbbaTeam = (player: PlayerDto, ibbaTeam: IbbaLinkStatusDto['teams'][number]) => {
    const key = (name: string) => name.trim().toLowerCase()
    const base = ibbaTeam.teamName.trim()
    const sameNameIbbaTeams = (ibbaLinks[player.id]?.teams ?? []).filter((it) => key(it.teamName) === key(base)).length
    const sameNameExistingTeams = (player.teams ?? []).filter((pt) => key(pt.name) === key(base)).length
    const isAmbiguous = sameNameIbbaTeams > 1 || sameNameExistingTeams > 0
    return isAmbiguous && ibbaTeam.ibbaLeagueName ? `${base} - ${ibbaTeam.ibbaLeagueName.trim()}` : base
  }

  const mapIbbaTeamToNew = async (playerId: number, ibbaTeamLinkId: number, teamName: string) => {
    if (!teamName.trim()) return
    setIbbaBusy((prev) => ({ ...prev, [playerId]: true }))
    try {
      const { data: team } = await api.post<TeamDto>('/teams', { name: teamName.trim() })
      await api.post(`/teams/${team.id}/players/${playerId}`, {})
      setAllTeams((prev) => [...prev, team])
      const { data } = await api.put<IbbaLinkStatusDto>(`/ibba/team-links/${ibbaTeamLinkId}`, { teamId: team.id })
      setIbbaLinks((prev) => ({ ...prev, [playerId]: data }))
    } catch {
      setIbbaError((prev) => ({ ...prev, [playerId]: 'Could not create that team.' }))
    } finally {
      // The new team is created and joined before the IBBA link step - show
      // it on the player right away, even if the link step itself failed.
      await refreshPlayer(playerId)
      setIbbaBusy((prev) => ({ ...prev, [playerId]: false }))
    }
  }

  if (loading) {
    return (
      <div className="page-container">
        <h2>👤 Player Profiles</h2>
        <p>Loading...</p>
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
                      {allTeams.filter((t) => !(player.teams ?? []).some((pt) => pt.id === t.id)).length > 0 && (
                        <select
                          defaultValue=""
                          disabled={teamBusy}
                          onChange={(e) => e.target.value && addExistingTeam(player.id, Number(e.target.value))}
                        >
                          <option value="">Add existing team...</option>
                          {allTeams
                            .filter((t) => !(player.teams ?? []).some((pt) => pt.id === t.id))
                            .map((t) => (
                              <option key={t.id} value={t.id}>{t.name}</option>
                            ))}
                        </select>
                      )}
                      <div className="flex gap-1">
                        <input
                          type="number"
                          placeholder="Jersey #"
                          value={pickerJerseyNumber}
                          onChange={(e) => setPickerJerseyNumber(e.target.value)}
                          style={{ maxWidth: '9rem' }}
                        />
                        <input
                          type="text"
                          placeholder="New team name (e.g. U16)"
                          value={newTeamName}
                          onChange={(e) => setNewTeamName(e.target.value)}
                        />
                        <button className="submit-btn" disabled={teamBusy} onClick={() => createAndAddTeam(player.id)}>
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
                  {ibbaLinks[player.id]!.lastSyncError && (
                    <p className="error">{ibbaLinks[player.id]!.lastSyncError}</p>
                  )}
                  {ibbaLinks[player.id]!.teams.map((t) => (
                    <div className="ibba-team-row" key={t.id}>
                      <TeamCrest
                        logoUrl={t.teamLogoUrl}
                        showIbbaMark
                        size="sm"
                        onClick={t.ibbaLeagueUrl ? () => setStandingsFor({ leagueUrl: t.ibbaLeagueUrl!, leagueName: t.ibbaLeagueName ?? '', teamName: t.teamName, teamUrl: t.teamUrl }) : undefined}
                      />
                      <div style={{ flex: 1, minWidth: '10rem' }}>
                        <div className="team-name-clamp" style={{ fontWeight: 700, fontSize: '0.9rem' }} dir="rtl" title={t.teamName}>{t.teamName}</div>
                        {t.ibbaLeagueName && (
                          <button
                            className="league-chip"
                            style={{ marginTop: '0.3rem', padding: '0.15rem 0.6rem 0.15rem 0.4rem', fontSize: '0.7rem' }}
                            onClick={() => setStandingsFor({ leagueUrl: t.ibbaLeagueUrl!, leagueName: t.ibbaLeagueName ?? '', teamName: t.teamName, teamUrl: t.teamUrl })}
                          >
                            <svg className="icon" style={{ width: 11, height: 11 }}><use href="#i-trophy" /></svg>
                            <span dir="rtl">{t.ibbaLeagueName}</span>
                            {!!t.position && t.position > 0 && ` · ${t.position} of ${t.totalTeams}`}
                          </button>
                        )}
                      </div>
                      {t.linkedTeamId ? (
                        <span className="ibba-synced-to" title={t.linkedTeamName ?? undefined}>
                          Synced to <b dir="auto">{t.linkedTeamName}</b>
                        </span>
                      ) : (
                        <div className="flex gap-1" style={{ flexWrap: 'wrap' }}>
                          {(player.teams ?? []).length > 0 && (
                            <select
                              className="ibba-link-select"
                              defaultValue=""
                              disabled={ibbaBusy[player.id]}
                              onChange={(e) => e.target.value && mapIbbaTeamToExisting(player.id, t.id, Number(e.target.value))}
                            >
                              <option value="">Link to existing team...</option>
                              {(player.teams ?? []).map((pt) => (
                                <option key={pt.id} value={pt.id}>{pt.name}</option>
                              ))}
                            </select>
                          )}
                          {(() => {
                            const newTeamName = nameForNewIbbaTeam(player, t)
                            return (
                              <button
                                className="submit-btn ibba-create-team-btn"
                                disabled={ibbaBusy[player.id]}
                                onClick={() => mapIbbaTeamToNew(player.id, t.id, newTeamName)}
                                title={`Create team "${newTeamName}"`}
                              >
                                <span>Create new team</span>
                                <span className="ibba-create-team-name team-name-clamp" dir="auto">{newTeamName}</span>
                              </button>
                            )
                          })()}
                        </div>
                      )}
                    </div>
                  ))}
                  <div className="flex gap-1" style={{ marginTop: '0.75rem' }}>
                    <button className="add-team-btn" disabled={ibbaBusy[player.id]} onClick={() => syncIbba(player.id)}>
                      🔄 Sync Now
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
                        {ibbaBusy[player.id] ? 'Checking...' : 'Preview'}
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
                        {ibbaNewBusy ? 'Creating...' : 'Create Player'}
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
                      {ibbaNewBusy ? 'Checking...' : 'Preview'}
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
              {saving ? 'Saving...' : 'Save Player'}
            </button>
            <button className="nav-btn" onClick={() => setShowAddForm(false)}>Cancel</button>
          </div>
        </div>
      )}

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
