import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'
import { TOKEN_STORAGE_KEY } from '../api/client'

// One connection to the server's live hub for the whole app. The server sends
// "GameChanged <gameId>" the moment a watched player's game changes (score,
// box score, a shot, status, on court); screens then reload that game through
// the API as usual. While the connection is down, screens fall back to polling
// (see useLiveRefresh), so nothing depends on it being up.

type Target = { player: string } | { share: string }
type Listener = (gameId: string) => void

const hubUrl = (import.meta.env.VITE_API_URL || '/api').replace(/\/api\/?$/, '') + '/hubs/live'

const listeners = new Set<Listener>()
const connectedListeners = new Set<(connected: boolean) => void>()
const watched = new Map<string, { target: Target; count: number }>()
let connection: HubConnection | null = null

const keyOf = (target: Target) => ('player' in target ? `player:${target.player}` : `share:${target.share}`)

function join(target: Target) {
  if (connection?.state !== HubConnectionState.Connected) return
  const call = 'player' in target ? connection.invoke('WatchPlayer', target.player) : connection.invoke('WatchShare', target.share)
  call.catch(() => { /* not allowed or gone - polling still covers it */ })
}

function setConnected(connected: boolean) {
  connectedListeners.forEach((listener) => listener(connected))
}

async function start() {
  if (!connection) return
  try {
    await connection.start()
    watched.forEach(({ target }) => join(target))
    setConnected(true)
  } catch {
    // The server is unreachable for now - try again shortly.
    window.setTimeout(start, 10000)
  }
}

function ensureConnection() {
  if (connection) return
  connection = new HubConnectionBuilder()
    .withUrl(hubUrl, {
      accessTokenFactory: () => localStorage.getItem(TOKEN_STORAGE_KEY) ?? '',
      withCredentials: false, // signed in by token, not cookies
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.None)
    .build()
  connection.on('GameChanged', (gameId: string) => listeners.forEach((listener) => listener(gameId)))
  connection.onreconnecting(() => setConnected(false))
  connection.onreconnected(() => {
    watched.forEach(({ target }) => join(target)) // groups don't survive a new connection
    setConnected(true)
  })
  connection.onclose(() => {
    setConnected(false)
    window.setTimeout(start, 10000)
  })
  start()
}

function watch(targets: Target[]) {
  ensureConnection()
  for (const target of targets) {
    const key = keyOf(target)
    const entry = watched.get(key)
    if (entry) entry.count++
    else {
      watched.set(key, { target, count: 1 })
      join(target)
    }
  }
  return () => {
    for (const target of targets) {
      const key = keyOf(target)
      const entry = watched.get(key)
      if (!entry) continue
      if (--entry.count === 0) {
        watched.delete(key)
        if ('player' in target && connection?.state === HubConnectionState.Connected)
          connection.invoke('UnwatchPlayer', target.player).catch(() => {})
      }
    }
  }
}

// Calls onChange(gameId) whenever a game of these players (or of this share
// link) changes. Returns whether the live connection is up - when it isn't,
// the caller should poll instead.
export function useLiveGames(targets: Target[], onChange: Listener): boolean {
  const [connected, setConnectedState] = useState(() => connection?.state === HubConnectionState.Connected)
  const latest = useRef(onChange)
  latest.current = onChange
  const key = targets.map(keyOf).sort().join(',')

  useEffect(() => {
    if (!key) return
    const listener: Listener = (gameId) => latest.current(gameId)
    listeners.add(listener)
    connectedListeners.add(setConnectedState)
    const unwatch = watch(targets)
    setConnectedState(connection?.state === HubConnectionState.Connected)
    return () => {
      listeners.delete(listener)
      connectedListeners.delete(setConnectedState)
      unwatch()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])

  return connected
}
