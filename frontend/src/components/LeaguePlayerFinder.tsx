import { useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import type { IbbaPreviewDto } from '../api/types'
import { Busy } from './BouncingBall'
import { looksLikeLink } from '../utils/leagueLinks'

const MIN_SEARCH = 3


const messageOf = (err: unknown) => (err as { response?: { data?: { message?: string } } })?.response?.data?.message

// One box for finding a player on a league site: type their name to search,
// or paste the address of their page. A name lists the matches (photo, birth
// year, team and league) as you type; picking one - or a pasted link - hands
// the same preview to onFound, which shows the "Found on …" card.
export default function LeaguePlayerFinder({
  value,
  onChange,
  onLink,
  onPick,
  busy,
  inputId,
  label,
  leagues,
  urlExamples,
}: {
  value: string
  onChange: (value: string) => void
  // Check a pasted / typed address.
  onLink: (url: string) => void
  // A player picked from the search results.
  onPick: (preview: IbbaPreviewDto) => void
  busy: boolean
  inputId: string
  label: string
  leagues: string
  urlExamples: string
}) {
  const query = value.trim().replace(/\s+/g, ' ')
  const isLink = looksLikeLink(value)
  const canSearch = !isLink && query.length >= MIN_SEARCH
  const [search, setSearch] = useState<{ query: string; results: IbbaPreviewDto[] | null; error: string | null }>({ query: '', results: null, error: null })
  const [runNow, setRunNow] = useState(0)
  const immediate = useRef(false)

  // Search a moment after typing stops (or right away on Find / Enter).
  useEffect(() => {
    if (!canSearch) return
    let current = true
    const delay = immediate.current ? 0 : 450
    immediate.current = false
    const timer = setTimeout(() => {
      setSearch({ query, results: null, error: null })
      api
        .get<IbbaPreviewDto[]>('/ibba/search', { params: { name: query } })
        .then((res) => current && setSearch({ query, results: res.data, error: null }))
        .catch((err) => current && setSearch({ query, results: [], error: messageOf(err) ?? 'Search failed - try again, or paste the player\'s link.' }))
    }, delay)
    return () => {
      current = false
      clearTimeout(timer)
    }
  }, [query, canSearch, runNow])

  const shown = canSearch && search.query === query ? search : null
  const searching = canSearch && (!shown || shown.results === null)
  const find = () => {
    if (isLink) return onLink(value.trim())
    immediate.current = true
    setRunNow((n) => n + 1)
  }

  return (
    <div className="player-finder">
      <label className="visually-hidden" htmlFor={inputId}>{label}</label>
      <div className="ibba-connect-form">
        <input
          id={inputId}
          type="text"
          dir="auto"
          autoComplete="off"
          className={isLink ? 'looks-like-link' : undefined}
          placeholder={`Type the player's name, or paste their ${leagues} link`}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter') find() }}
          onPaste={(e) => {
            const pasted = e.clipboardData.getData('text').trim()
            if (!pasted || !looksLikeLink(pasted)) return
            e.preventDefault()
            onChange(pasted)
            onLink(pasted) // a pasted link is checked right away
          }}
        />
        <button className="submit-btn" disabled={busy || (isLink ? !query : !canSearch)} onClick={find}>
          {busy ? <Busy>Checking…</Busy> : 'Find'}
        </button>
      </div>

      {searching && <p className="finder-status"><Busy>Searching {leagues}…</Busy></p>}

      {shown?.error && <p className="error">{shown.error}</p>}

      {shown?.results && shown.results.length === 0 && !shown.error && (
        <p className="finder-status">
          No current {leagues} player named <bdi>“{query}”</bdi>. Check the spelling (names are in Hebrew), or paste their link.
        </p>
      )}

      {shown?.results && shown.results.length > 0 && (
        <ul className="finder-results" aria-label="Players found">
          {shown.results.map((p) => (
            <li key={p.playerUrl}>
              <button type="button" className="finder-result" onClick={() => onPick(p)} disabled={busy}>
                {p.photoUrl ? (
                  <img className="finder-avatar" src={p.photoUrl} alt="" />
                ) : (
                  <span className="finder-avatar">{p.playerName.split(' ').filter(Boolean).map((w) => w[0]).slice(0, 2).join('')}</span>
                )}
                <span className="finder-text">
                  <span className="finder-name" dir="auto">{p.playerName}</span>
                  <span className="finder-teams">
                    {p.teams.map((t) => (
                      <span key={t.teamUrl || t.teamName} className="finder-team">
                        <bdi>{t.teamName}</bdi>
                        {t.leagueName && <> · <bdi>{t.leagueName}</bdi></>}
                      </span>
                    ))}
                  </span>
                </span>
                <span className="finder-side">
                  {p.dateOfBirth && <span className="finder-born">{new Date(p.dateOfBirth).getUTCFullYear()}</span>}
                  {p.alreadyLinkedTo && <span className="finder-added">Already added</span>}
                </span>
              </button>
            </li>
          ))}
        </ul>
      )}

      {!canSearch && !isLink && (
        <p className="found-hint">Search by the player&apos;s name in Hebrew - or open them on the league&apos;s site ({urlExamples}) and paste the address.</p>
      )}
    </div>
  )
}
