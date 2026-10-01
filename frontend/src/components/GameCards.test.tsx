import { fireEvent, render, screen, within } from '@testing-library/react'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { makeGame, makeStats } from '../test/fixtures'
import { AveragesCard, ScheduleGameCards, StatsGameCards } from './GameCards'

const inRouter = (ui: ReactElement) => render(<MemoryRouter>{ui}</MemoryRouter>)

describe('phone game cards', () => {
  it('a finished game shows the result and box score, and opens the game', () => {
    const game = makeGame({ id: '42', status: 'Completed', teamScore: 70, opponentScore: 64, playerStats: [makeStats({ totalPoints: 17 })] })
    inRouter(<StatsGameCards games={[game]} linkFor={(g) => `/games/${g.id}`} />)

    const card = screen.getByRole('link')
    expect(card).toHaveAttribute('href', '/games/42')
    expect(within(card).getByText(/W 70.64/)).toBeInTheDocument()
    expect(within(card).getByText('17')).toBeInTheDocument()
    expect(within(card).getByText('Eagles')).toBeInTheDocument()
  })

  it('a loss reads as a loss, and a game without a box score says so', () => {
    const game = makeGame({ status: 'Completed', teamScore: 50, opponentScore: 60, playerStats: [] })
    inRouter(<StatsGameCards games={[game]} linkFor={() => '/x'} />)
    expect(screen.getByText(/L 50.60/)).toBeInTheDocument()
    expect(screen.getByText('No box score recorded')).toBeInTheDocument()
  })

  it('a finished friendly is marked as not counted', () => {
    const game = makeGame({ status: 'Completed', gameType: 'Friendly', teamScore: 1, opponentScore: 0, playerStats: [makeStats()] })
    inRouter(<StatsGameCards games={[game]} linkFor={() => '/x'} />)
    expect(screen.getByText('not counted')).toBeInTheDocument()
  })

  it('schedule cards show an unplayed game as upcoming, with home/away and actions', () => {
    const onEdit = vi.fn()
    inRouter(
      <ScheduleGameCards
        games={[makeGame({ status: 'Upcoming', isHomeGame: false })]}
        actions={() => <button onClick={onEdit}>Edit</button>}
      />,
    )
    expect(screen.getByText('Upcoming')).toBeInTheDocument()
    expect(screen.getByText(/Away/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }))
    expect(onEdit).toHaveBeenCalledOnce()
  })

  it('the averages card shows how many games it covers', () => {
    inRouter(<AveragesCard averages={{ count: 1, pts: '17.0', fgm: '4.0', fga: '9.0', tpm: '2.0', tpa: '5.0', ftm: '3.0', fta: '4.0', reb: '7.0', ast: '4.0', stl: '2.0', blk: '1.0', to: '3.0' }} />)
    expect(screen.getByText(/1 game$/)).toBeInTheDocument()
    expect(screen.getByText('17.0')).toBeInTheDocument()
  })
})
