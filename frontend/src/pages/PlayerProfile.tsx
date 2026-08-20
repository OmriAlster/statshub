import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { InviteDto, PlayerDto, TeamDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'

const emptyForm = {
  firstName: '',
  lastName: '',
  jerseyNumber: 0,
  position: '',
  dateOfBirth: new Date().toISOString().split('T')[0],
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

  const [invites, setInvites] = useState<Record<number, InviteDto>>({})
  const [parentInvites, setParentInvites] = useState<Record<number, InviteDto>>({})
  const [shareLinks, setShareLinks] = useState<Record<number, string>>({})
  const [busyPlayerId, setBusyPlayerId] = useState<number | null>(null)

  const [teamPickerOpenFor, setTeamPickerOpenFor] = useState<number | null>(null)
  const [newTeamName, setNewTeamName] = useState('')
  const [teamBusy, setTeamBusy] = useState(false)

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
        jerseyNumber: Number(form.jerseyNumber),
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

  const attachTeamToPlayer = (playerId: number, team: TeamDto) => {
    setPlayers((prev) =>
      prev.map((p) =>
        p.id === playerId && !(p.teams ?? []).some((t) => t.id === team.id)
          ? { ...p, teams: [...(p.teams ?? []), team] }
          : p
      )
    )
  }

  const addExistingTeam = async (playerId: number, teamId: number) => {
    const team = allTeams.find((t) => t.id === teamId)
    if (!team) return
    setTeamBusy(true)
    try {
      await api.post(`/teams/${teamId}/players/${playerId}`)
      attachTeamToPlayer(playerId, team)
      setTeamPickerOpenFor(null)
    } catch {
      setError('Could not add player to that team.')
    } finally {
      setTeamBusy(false)
    }
  }

  const createAndAddTeam = async (playerId: number) => {
    if (!newTeamName.trim()) return
    setTeamBusy(true)
    try {
      const { data: team } = await api.post<TeamDto>('/teams', { name: newTeamName.trim() })
      await api.post(`/teams/${team.id}/players/${playerId}`)
      setAllTeams((prev) => [...prev, team])
      attachTeamToPlayer(playerId, team)
      setNewTeamName('')
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
    } catch {
      setError('Could not remove player from that team.')
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
              <div className="avatar-lg">{player.firstName[0]}{player.lastName[0]}</div>
              <div className="profile-id">
                <h3>{player.firstName} {player.lastName} <span className="player-card-number">#{player.jerseyNumber}</span></h3>
                <p className="profile-id-sub">{player.position || 'Player'}</p>
              </div>
            </div>

            <div className="profile-info">
              <p>📅 Born: {new Date(player.dateOfBirth).toLocaleDateString()}</p>
              {player.height && <p>📏 Height: {player.height} cm</p>}
              {player.weight && <p>⚖️ Weight: {player.weight} kg</p>}
            </div>

            <div className="team-section">
              <div className="team-chip-row">
                {(player.teams ?? []).map((t) => (
                  <span className="team-chip" key={t.id}>
                    {t.name}
                    {!isPlayerRole && (
                      <button className="team-chip-remove" onClick={() => removeTeam(player.id, t.id)} title="Remove from team">
                        ×
                      </button>
                    )}
                  </span>
                ))}
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
                          type="text"
                          placeholder="New team name (e.g. U16)"
                          value={newTeamName}
                          onChange={(e) => setNewTeamName(e.target.value)}
                        />
                        <button className="submit-btn" disabled={teamBusy} onClick={() => createAndAddTeam(player.id)}>
                          Add
                        </button>
                        <button className="nav-btn" onClick={() => { setTeamPickerOpenFor(null); setNewTeamName('') }}>
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
            <h3>➕ Add New Player</h3>
            <p>Manage multiple players</p>
            <button onClick={() => setShowAddForm(true)}>Add Player</button>
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
              Jersey #
              <input type="number" value={form.jerseyNumber} onChange={(e) => setForm({ ...form, jerseyNumber: Number(e.target.value) })} />
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
    </div>
  )
}
