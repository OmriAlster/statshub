import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { makeGame, makePlayer, makeStats } from '../test/fixtures'
import EditGameSheet from './EditGameSheet'

vi.mock('../api/client', () => ({ api: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() } }))

const player = makePlayer({ id: 'p1', firstName: 'Idan' })
const line = { playerId: 'p1', number: 8, played: true, starter: true, points: 11, freeThrows: 5, twoPointers: 3, threePointers: 0, fouls: 1, sheetValid: true }
const open = (overrides: Parameters<typeof makeGame>[0]) =>
  render(<EditGameSheet player={player} game={makeGame({ isFromIbba: true, status: 'Completed', teamScore: 53, opponentScore: 84, ...overrides })} onGameUpdated={() => {}} onClose={() => {}} />)

describe('edit game: where the box score comes from', () => {
  it("nothing recorded: IBBA's line first, then the two ways to record it", () => {
    open({ playerStats: [], scoresheets: [line] })
    expect(screen.getByText('Not recorded')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: "Use IBBA's stats" })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Type the numbers/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Mark shots on the court/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Clear Idan's stats/ })).not.toBeInTheDocument()
  })

  it('recorded numbers that differ from the sheet are flagged, side by side', () => {
    const stats = makeStats({ playerId: 'p1', fieldGoalsMade: 2, threePointersMade: 0, freeThrowsMade: 5, totalPoints: 9, fouls: 1, source: null, scoresheet: line })
    open({ playerStats: [stats], scoresheets: [line] })
    expect(screen.getByText('Differs from IBBA')).toBeInTheDocument()
    expect(screen.getByRole('table', { name: /against IBBA's scoresheet/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: "Use IBBA's stats instead" })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: "Clear Idan's stats for this game" })).toBeInTheDocument()
  })

  it("the sheet's own numbers say so, and ask for what's missing", () => {
    const stats = makeStats({ playerId: 'p1', source: 'scoresheet', fieldGoalsAttempted: null, totalRebounds: null, minutesPlayed: null, scoresheet: line })
    open({ playerStats: [stats], scoresheets: [line] })
    expect(screen.getByText('From IBBA · #8')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: "Add what's missing" })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Use IBBA's stats/ })).not.toBeInTheDocument()
  })

  it("on the sheet without entering: said so, and IBBA's stats aren't offered", () => {
    open({ playerStats: [], scoresheets: [{ ...line, played: false }] })
    expect(screen.getByText(/not entering the game/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: "Use IBBA's stats" })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Type the numbers/ })).toBeInTheDocument()
  })

  it("a synced game's details aren't editable; your own game's are", () => {
    open({ playerStats: [] })
    expect(screen.getByText('Synced from IBBA')).toBeInTheDocument()
    expect(screen.queryByLabelText('Opponent')).not.toBeInTheDocument()
  })
})
