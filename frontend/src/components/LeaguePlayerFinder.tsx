import { useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import type { IbbaPreviewDto } from '../api/types'
import { Busy } from './BouncingBall'
import { looksLikeLink } from '../utils/leagueLinks'

const MIN_SEARCH = 3
const TYPING_PAUSE_MS = 600

const messageOf = (err: unknown) => (err as { response?: { data?: { message?: string } } })?.response?.data?.message

interface PlayerHit {
  leagueKey: string
  leagueName: string
  playerName: string
  playerUrl: string
}

// Each match's card (its page on the league site, ~3 s when the site hasn't
// cached it), kept for the visit so going back to the list doesn't fetch it
// again. 'gone' = no current team (a past season's player) - left out.
type Detail = { state: 'loading' } | { state: 'ready'; preview: IbbaPreviewDto } | { state: 'gone' } | { state: 'failed' }
const details = new Map<string, { detail: Detail; at: number }>()
const DETAIL_TTL_MS = 10 * 60 * 1000
const freshDetail = (url: string) => {
  const entry = details.get(url)
  return entry && Date.now() - entry.at < DETAIL_TTL_MS ? entry.detail : undefined
}

const initials = (name: string) => name.split(' ').filter(Boolean).map((w) => w[0]).slice(0, 2).join('')

// One box for finding a player on a league site: type their name to search,
// or paste the address of their page. A name lists the matches at once, and
// each fills in (photo, birth year, team and league) as its page arrives;
// picking one - or a pasted link - shows the "Found on …" card.
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
  const [search, setSearch] = useState<{ query: string; hits: PlayerHit[] | null; error: string | null }>({ query: '', hits: null, error: null })
  const [runNow, setRunNow] = useState(0)
  const immediate = useRef(false)
  const [, redraw] = useState(0)

  // Search a moment after typing stops (or right away on Find / Enter).
  useEffect(() => {
    if (!canSearch) return
    let current = true
    const delay = immediate.current ? 0 : TYPING_PAUSE_MS
    immediate.current = false
    const timer = setTimeout(() => {
      setSearch({ query, hits: null, error: null })
      api
        .get<PlayerHit[]>('/ibba/search', { params: { name: query } })
        .then((res) => current && setSearch({ query, hits: res.data, error: null }))
        .catch((err) => current && setSearch({ query, hits: [], error: messageOf(err) ?? "Search failed - try again, or paste the player's link." }))
    }, delay)
    return () => {
      current = false
      clearTimeout(timer)
    }
  }, [query, canSearch, runNow])

  // Each match's card, all at once - the rows fill in as they arrive.
  useEffect(() => {
    for (const hit of search.hits ?? []) {
      if (freshDetail(hit.playerUrl)) continue
      const set = (detail: Detail) => {
        details.set(hit.playerUrl, { detail, at: Date.now() })
        redraw((n) => n + 1)
      }
      set({ state: 'loading' })
      api
        .get<IbbaPreviewDto>('/ibba/preview', { params: { playerUrl: hit.playerUrl } })
        .then((res) => set({ state: 'ready', preview: res.data }))
        .catch((err) => set((err as { response?: { status?: number } })?.response?.status === 400 ? { state: 'gone' } : { state: 'failed' }))
    }
  }, [search.hits])

  const shown = canSearch && search.query === query ? search : null
  const searching = canSearch && (!shown || shown.hits === null)
  const rows = (shown?.hits ?? [])
    .map((hit) => ({ hit, detail: freshDetail(hit.playerUrl) ?? ({ state: 'loading' } as Detail) }))
    .filter((r) => r.detail.state !== 'gone')

  const find = () => {
    if (isLink) return onLink(value.trim())
    immediate.current = true
    setRunNow((n) => n + 1)
  }
  const pick = (hit: PlayerHit, detail: Detail) => {
    // Picking may add them - their "Already added" is read fresh next time.
    details.delete(hit.playerUrl)
    if (detail.state === 'ready') onPick(detail.preview)
    else onLink(hit.playerUrl)
  }

  return (
    <div className="player-finder" data-guide="player-finder">
      <label className="visually-hidden" htmlFor={inputId}>{label}</label>
      <div className="ibba-connect-form">
        <input
          id={inputId}
          type="text"
          dir="auto"
          autoComplete="off"
          className={isLink ? 'looks-like-link' : undefined}
          placeholder={`Name or ${leagues} link`}
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

      {shown?.hits && rows.length === 0 && !shown.error && (
        <p className="finder-status">
          No current {leagues} player named <bdi>“{query}”</bdi>. Check the spelling (names are in Hebrew), or paste their link.
        </p>
      )}

      {rows.length > 0 && (
        <ul className="finder-results" aria-label="Players found">
          {rows.map(({ hit, detail }) => {
            const p = detail.state === 'ready' ? detail.preview : null
            // The main team's crest, as a badge on the photo.
            const crest = p && (p.teams.find((t) => !t.isAdditional) ?? p.teams[0])?.logoUrl
            return (
              <li key={hit.playerUrl}>
                <button type="button" className="finder-result" onClick={() => pick(hit, detail)} disabled={busy}>
                  <span className="finder-avatar-wrap">
                    {p?.photoUrl ? (
                      <img className="finder-avatar" src={p.photoUrl} alt="" />
                    ) : (
                      <span className="finder-avatar">{initials(hit.playerName)}</span>
                    )}
                    {crest && <img className="finder-crest" src={crest} alt="" />}
                  </span>
                  <span className="finder-text">
                    <span className="finder-name" dir="auto">{p?.playerName ?? hit.playerName}</span>
                    <span className="finder-teams">
                      {p ? (
                        p.teams.map((t) => (
                          <span key={t.teamUrl || t.teamName} className="finder-team">
                            <bdi>{t.teamName}</bdi>
                            {t.leagueName && <> · <bdi>{t.leagueName}</bdi></>}
                          </span>
                        ))
                      ) : detail.state === 'failed' ? (
                        <span className="finder-team">Couldn&apos;t load their team - tap to try again</span>
                      ) : (
                        <span className="finder-loading" role="status" aria-label="Loading team">
                          <span />
                        </span>
                      )}
                    </span>
                  </span>
                  <span className="finder-side">
                    {p?.dateOfBirth && <span className="finder-born">{new Date(p.dateOfBirth).getUTCFullYear()}</span>}
                    {p?.alreadyLinkedTo && <span className="finder-added">Already added</span>}
                  </span>
                </button>
              </li>
            )
          })}
        </ul>
      )}

      {!canSearch && !isLink && (
        <p className="found-hint">Search by the player&apos;s name in Hebrew - or open them on the league&apos;s site ({urlExamples}) and paste the address.</p>
      )}
    </div>
  )
}
