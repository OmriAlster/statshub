import { fireEvent, render, screen } from '@testing-library/react'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import GettingStarted from './GettingStarted'
import { GuideProvider, useGuide } from './GuideContext'

const auth = vi.hoisted(() => ({ user: { id: 'u1', role: 'Parent' } as { id: string; role: string } | null }))
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: auth.user }) }))

const withGuide = (ui: ReactElement) => render(<MemoryRouter><GuideProvider>{ui}</GuideProvider></MemoryRouter>)

describe('getting started', () => {
  beforeEach(() => {
    localStorage.clear()
    auth.user = { id: 'u1', role: 'Parent' }
  })

  it('ticks off what is already done and counts it', () => {
    withGuide(<GettingStarted hasPlayer hasIbba={false} hasTrackedGame={false} />)

    expect(screen.getByRole('heading', { name: /Getting started · 1 of 4/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Find them on IBBA' })).toHaveAttribute('href', '/players')
  })

  it('a player not on IBBA can skip that step', () => {
    withGuide(<GettingStarted hasPlayer hasIbba={false} hasTrackedGame={false} />)

    fireEvent.click(screen.getByRole('button', { name: 'Not on IBBA' }))

    expect(screen.getByRole('heading', { name: /2 of 4/ })).toBeInTheDocument()
  })

  it('a parent on their own can skip bringing the family', () => {
    withGuide(<GettingStarted hasPlayer hasIbba={false} hasTrackedGame={false} />)

    expect(screen.getByRole('link', { name: 'Send an invite' })).toHaveAttribute('href', '/players?invite=1')
    fireEvent.click(screen.getByRole('button', { name: 'Just me' }))

    expect(screen.getByRole('heading', { name: /2 of 4/ })).toBeInTheDocument()
  })

  it('is gone once all are done, or when closed', () => {
    const { unmount } = withGuide(<GettingStarted hasPlayer hasIbba hasTrackedGame hasFamily />)
    expect(screen.queryByText(/Getting started/)).not.toBeInTheDocument()
    unmount()

    withGuide(<GettingStarted hasPlayer={false} hasIbba={false} hasTrackedGame={false} />)
    fireEvent.click(screen.getByRole('button', { name: 'Hide getting started' }))
    expect(screen.queryByText(/Getting started/)).not.toBeInTheDocument()
  })

  it("isn't shown to a player's own login", () => {
    auth.user = { id: 'u2', role: 'Player' }
    withGuide(<GettingStarted hasPlayer={false} hasIbba={false} hasTrackedGame={false} />)
    expect(screen.queryByText(/Getting started/)).not.toBeInTheDocument()
  })
})

describe('the welcome', () => {
  beforeEach(() => {
    localStorage.clear()
    auth.user = { id: 'u1', role: 'Parent' }
  })

  function Probe() {
    const guide = useGuide()
    return (
      <>
        <span>{guide.welcomeOpen ? 'open' : 'closed'} · {guide.tourStep ?? 'no tour'}</span>
        <button onClick={guide.offerWelcome}>offer</button>
        <button onClick={guide.startTour}>start</button>
        <button onClick={guide.replay}>replay</button>
      </>
    )
  }

  it('is offered once; starting the tour closes it and begins at Find on IBBA', () => {
    withGuide(<Probe />)
    fireEvent.click(screen.getByText('offer'))
    expect(screen.getByText('open · no tour')).toBeInTheDocument()

    fireEvent.click(screen.getByText('start'))
    expect(screen.getByText('closed · find')).toBeInTheDocument()

    fireEvent.click(screen.getByText('offer')) // seen - not offered again
    expect(screen.getByText('closed · find')).toBeInTheDocument()
  })

  it('"?" brings it back', () => {
    withGuide(<Probe />)
    fireEvent.click(screen.getByText('start'))
    fireEvent.click(screen.getByText('replay'))
    expect(screen.getByText('open · no tour')).toBeInTheDocument()
  })
})
