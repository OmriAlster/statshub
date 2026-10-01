import { renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const post = vi.fn(() => Promise.resolve({ data: null }))
const get = vi.fn(() => Promise.resolve({ data: 'BAAA' })) // the server's public push key
vi.mock('../api/client', () => ({ api: { post: (...args: unknown[]) => post(...(args as [])), get: (...args: unknown[]) => get(...(args as [])) } }))

import { usePushNotifications } from './usePushNotifications'

// A phone that already has a push subscription saved (e.g. from before the
// server's copy was lost).
function fakePhone(permission: NotificationPermission, hasSubscription: boolean, subscribedWithKey?: number[]) {
  const subscription = hasSubscription
    ? {
        toJSON: () => ({ endpoint: 'https://push.example/abc', keys: { p256dh: 'key', auth: 'secret' } }),
        options: { applicationServerKey: subscribedWithKey ? new Uint8Array(subscribedWithKey).buffer : null },
        unsubscribe: vi.fn(() => Promise.resolve(true)),
      }
    : null
  Object.defineProperty(window, 'PushManager', { value: function PushManager() {}, configurable: true })
  Object.defineProperty(window, 'Notification', { value: { permission }, configurable: true })
  const created = { toJSON: () => ({ endpoint: 'https://push.example/new', keys: { p256dh: 'k2', auth: 's2' } }) }
  const pushManager = { getSubscription: () => Promise.resolve(subscription), subscribe: vi.fn(() => Promise.resolve(created)), subscription }
  Object.defineProperty(navigator, 'serviceWorker', {
    value: { ready: Promise.resolve({ pushManager }) },
    configurable: true,
  })
  return pushManager
}

describe('push notification subscription', () => {
  beforeEach(() => {
    post.mockClear()
    get.mockClear()
  })
  afterEach(() => vi.restoreAllMocks())

  it('re-sends the phone subscription to the server every time the app opens signed in', async () => {
    fakePhone('granted', true)
    const { result } = renderHook(() => usePushNotifications('7'))

    await waitFor(() => expect(post).toHaveBeenCalledOnce())
    expect(post).toHaveBeenCalledWith('/push/subscribe', { endpoint: 'https://push.example/abc', p256dh: 'key', auth: 'secret' })
    expect(result.current.subscribed).toBe(true)
  })

  it('re-sends again when a different account signs in on the same phone', async () => {
    fakePhone('granted', true)
    const { rerender } = renderHook(({ userId }) => usePushNotifications(userId), { initialProps: { userId: '7' } })
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1))

    rerender({ userId: '8' })
    await waitFor(() => expect(post).toHaveBeenCalledTimes(2))
  })

  it('sets up a subscription by itself when notifications are allowed but none exists', async () => {
    // Permission already granted means the Enable banner never shows, so
    // without this nothing would ever subscribe.
    const pushManager = fakePhone('granted', false)
    renderHook(() => usePushNotifications('7'))

    await waitFor(() => expect(post).toHaveBeenCalledWith('/push/subscribe', { endpoint: 'https://push.example/new', p256dh: 'k2', auth: 's2' }))
    expect(pushManager.subscribe).toHaveBeenCalledOnce()
  })

  it('replaces a subscription made with an old push key', async () => {
    // The server's key is "BAAA" (bytes 4,0,0); this phone subscribed with another.
    const pushManager = fakePhone('granted', true, [9, 9, 9])
    renderHook(() => usePushNotifications('7'))

    await waitFor(() => expect(post).toHaveBeenCalledWith('/push/subscribe', { endpoint: 'https://push.example/new', p256dh: 'k2', auth: 's2' }))
    expect(pushManager.subscription!.unsubscribe).toHaveBeenCalledOnce()
    expect(pushManager.subscribe).toHaveBeenCalledOnce()
  })

  it('keeps a subscription made with the current key', async () => {
    const pushManager = fakePhone('granted', true, [4, 0, 0])
    renderHook(() => usePushNotifications('7'))

    await waitFor(() => expect(post).toHaveBeenCalledWith('/push/subscribe', { endpoint: 'https://push.example/abc', p256dh: 'key', auth: 'secret' }))
    expect(pushManager.subscription!.unsubscribe).not.toHaveBeenCalled()
    expect(pushManager.subscribe).not.toHaveBeenCalled()
  })

  it.each([
    ['signed out', 'granted', true, undefined],
    ['notifications not allowed', 'denied', true, '7'],
    ['notifications not allowed and never subscribed', 'denied', false, '7'],
  ] as const)('sends nothing when %s', async (_case, permission, hasSubscription, userId) => {
    fakePhone(permission, hasSubscription)
    renderHook(() => usePushNotifications(userId))

    await new Promise((r) => setTimeout(r, 20))
    expect(post).not.toHaveBeenCalled()
  })
})
