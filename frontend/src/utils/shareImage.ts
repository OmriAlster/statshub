import { api } from '../api/client'

// The images a family shares (the player card, the season summary), drawn on
// a canvas - what's on screen is the picture that's saved. 1080 x 1350, the
// shape Instagram and WhatsApp show whole.

export const SHARE_W = 1080
export const SHARE_H = 1350

const C = {
  bg: '#0b0d14',
  card: '#1b1e30',
  line: '#2a2f47',
  text: '#eef0f6',
  muted: '#9aa1b5',
  faint: '#6b7290',
  orange: '#ff8a2b',
  lime: '#c6f432',
  gold: '#ffd23f',
  green: '#3fd08a',
  red: '#ff5a73',
}
const DISPLAY = "'Space Grotesk', 'Heebo', sans-serif"
const SANS = "'Inter', 'Heebo', sans-serif"
// Right-to-left when the first letter is Hebrew ("מכבי חיפה"), left-to-right
// when it's Latin ("vs מכבי חיפה" - the name kept whole by isolate()).
const STARTS_HEBREW = /^[^A-Za-z֐-׿]*[֐-׿]/
export const isolate = (s: string) => `⁨${s}⁩`

// IBBA's images come through our server (IBBA sends no CORS headers, and a
// canvas can't export what it can't read); others are asked for directly.
export async function loadImage(url?: string | null): Promise<HTMLImageElement | null> {
  if (!url) return null
  try {
    let src = url
    if (/^https:\/\/ibasketball\.co\.il\//.test(url)) {
      const { data } = await api.get<Blob>('/images', { params: { url }, responseType: 'blob' })
      src = URL.createObjectURL(data)
    }
    const img = new Image()
    img.crossOrigin = 'anonymous'
    img.src = src
    await img.decode()
    return img
  } catch {
    return null
  }
}

async function fontsReady() {
  await Promise.all(['800 40px Heebo', '700 40px Heebo', '400 40px Heebo', "700 40px 'Space Grotesk'", '600 40px Inter', '800 40px Inter']
    .map((f) => document.fonts.load(f).catch(() => null)))
}

function canvas() {
  const el = document.createElement('canvas')
  el.width = SHARE_W
  el.height = SHARE_H
  return [el, el.getContext('2d')!] as const
}

// Text that may be Hebrew: drawn right-to-left from the right edge it's given
// when it is, left-to-right from the left one otherwise; shrunk to fit.
function text(ctx: CanvasRenderingContext2D, value: string, x: number, y: number, opts: { font: string; size: number; color: string; maxWidth?: number; align?: CanvasTextAlign }) {
  let size = opts.size
  ctx.font = `${opts.font.replace('{size}', String(size))}`
  while (opts.maxWidth && ctx.measureText(value).width > opts.maxWidth && size > 18) {
    size -= 2
    ctx.font = opts.font.replace('{size}', String(size))
  }
  ctx.fillStyle = opts.color
  ctx.direction = STARTS_HEBREW.test(value) ? 'rtl' : 'ltr'
  ctx.textAlign = opts.align ?? 'left'
  ctx.fillText(value, x, y)
  ctx.direction = 'ltr'
}

function roundRect(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number, fill: string, stroke?: string) {
  ctx.beginPath()
  ctx.roundRect(x, y, w, h, r)
  ctx.fillStyle = fill
  ctx.fill()
  if (stroke) {
    ctx.strokeStyle = stroke
    ctx.lineWidth = 3
    ctx.stroke()
  }
}

function background(ctx: CanvasRenderingContext2D, glow: string) {
  ctx.fillStyle = C.bg
  ctx.fillRect(0, 0, SHARE_W, SHARE_H)
  const g = ctx.createRadialGradient(SHARE_W * 0.85, 0, 0, SHARE_W * 0.85, 0, SHARE_W * 1.1)
  g.addColorStop(0, glow)
  g.addColorStop(1, 'rgba(11,13,20,0)')
  ctx.fillStyle = g
  ctx.fillRect(0, 0, SHARE_W, SHARE_H)
  // a court's hardwood, faintly
  ctx.strokeStyle = 'rgba(255,255,255,0.025)'
  ctx.lineWidth = 2
  for (let x = -SHARE_H; x < SHARE_W; x += 36) {
    ctx.beginPath()
    ctx.moveTo(x, SHARE_H)
    ctx.lineTo(x + SHARE_H * 0.6, 0)
    ctx.stroke()
  }
}

function footer(ctx: CanvasRenderingContext2D, label: string) {
  text(ctx, label, 72, SHARE_H - 64, { font: `600 {size}px ${SANS}`, size: 28, color: C.faint })
  text(ctx, 'StatsHub', SHARE_W - 72, SHARE_H - 64, { font: `700 {size}px ${DISPLAY}`, size: 34, color: C.orange, align: 'right' })
}

function circleImage(ctx: CanvasRenderingContext2D, img: HTMLImageElement | null, x: number, y: number, r: number, fallback: string) {
  ctx.save()
  ctx.beginPath()
  ctx.arc(x, y, r, 0, Math.PI * 2)
  ctx.closePath()
  ctx.fillStyle = '#262b44'
  ctx.fill()
  ctx.clip()
  if (img) {
    const s = Math.max((r * 2) / img.width, (r * 2) / img.height)
    ctx.drawImage(img, x - (img.width * s) / 2, y - (img.height * s) / 2, img.width * s, img.height * s)
  } else {
    text(ctx, fallback, x, y + r * 0.32, { font: `800 {size}px ${SANS}`, size: Math.round(r * 0.9), color: C.orange, align: 'center' })
  }
  ctx.restore()
  ctx.beginPath()
  ctx.arc(x, y, r, 0, Math.PI * 2)
  ctx.strokeStyle = C.orange
  ctx.lineWidth = 6
  ctx.stroke()
}

export interface PlayerCardData {
  playerName: string
  initials: string
  photoUrl?: string | null
  // One line each (up to three) - the player's total shows every team.
  teams: { name: string; crestUrl?: string | null; leagueName?: string | null }[]
  jersey?: number | null
  season: string
  // The three numbers on the card - the averages when there are lines,
  // otherwise the team's season (games, record, place).
  numbers: { value: string; label: string }[]
  note?: string | null
}

export async function drawPlayerCard(d: PlayerCardData): Promise<string> {
  await fontsReady()
  const [photo, ...crests] = await Promise.all([loadImage(d.photoUrl), ...d.teams.map((t) => loadImage(t.crestUrl))])
  const [el, ctx] = canvas()
  background(ctx, 'rgba(255,138,43,0.35)')

  // frame
  roundRect(ctx, 40, 40, SHARE_W - 80, SHARE_H - 80, 48, 'rgba(0,0,0,0)', 'rgba(255,138,43,0.45)')

  circleImage(ctx, photo, SHARE_W / 2, 330, 190, d.initials)
  if (d.jersey != null) {
    roundRect(ctx, SHARE_W / 2 + 110, 430, 150, 110, 30, C.orange)
    text(ctx, String(d.jersey), SHARE_W / 2 + 185, 512, { font: `700 {size}px ${DISPLAY}`, size: 80, color: '#1a0d00', align: 'center' })
  }

  text(ctx, d.playerName, SHARE_W / 2, 640, { font: `800 {size}px ${SANS}`, size: 76, color: C.text, align: 'center', maxWidth: SHARE_W - 160 })

  // the team(s): name, then its crest on the right, each line centered; the
  // league under it when there's a single team
  let teamY = 715
  const single = d.teams.length === 1
  d.teams.forEach((t, i) => {
    const crest = crests[i]
    const size = single ? 40 : 36
    ctx.font = `700 ${size}px ${SANS}`
    const nameW = Math.min(ctx.measureText(t.name).width, SHARE_W - 300)
    const lineW = nameW + (crest ? 84 : 0)
    const left = (SHARE_W - lineW) / 2
    text(ctx, t.name, left + nameW / 2, teamY, { font: `700 {size}px ${SANS}`, size, color: C.muted, align: 'center', maxWidth: SHARE_W - 300 })
    if (crest) {
      roundRect(ctx, left + nameW + 20, teamY - 48, 64, 64, 16, '#ffffff')
      ctx.drawImage(crest, left + nameW + 24, teamY - 44, 56, 56)
    }
    if (single && t.leagueName && !t.name.includes(t.leagueName)) {
      text(ctx, t.leagueName, SHARE_W / 2, teamY + 52, { font: `400 {size}px ${SANS}`, size: 32, color: C.faint, align: 'center' })
      teamY += 52
    }
    teamY += single ? 0 : 76
  })

  // the numbers
  const top = Math.max(860, teamY + (single ? 100 : 40))
  const w = (SHARE_W - 144 - 2 * 28) / 3
  d.numbers.slice(0, 3).forEach((n, i) => {
    const x = 72 + i * (w + 28)
    roundRect(ctx, x, top, w, 230, 32, 'rgba(0,0,0,0.38)', C.line)
    text(ctx, n.value, x + w / 2, top + 135, { font: `700 {size}px ${DISPLAY}`, size: 96, color: C.lime, align: 'center', maxWidth: w - 30 })
    text(ctx, n.label.toUpperCase(), x + w / 2, top + 190, { font: `700 {size}px ${SANS}`, size: 28, color: C.muted, align: 'center' })
  })
  if (d.note) text(ctx, d.note, SHARE_W / 2, top + 290, { font: `400 {size}px ${SANS}`, size: 30, color: C.faint, align: 'center', maxWidth: SHARE_W - 160 })

  footer(ctx, d.season)
  return el.toDataURL('image/png')
}

export interface WrappedData {
  period: string // "October" / "2026-27 season"
  playerName: string
  photoUrl?: string | null
  initials: string
  games: number
  wins: number
  lines: { label: string; value: string; detail?: string }[]
}

export async function drawWrapped(d: WrappedData): Promise<string> {
  await fontsReady()
  const photo = await loadImage(d.photoUrl)
  const [el, ctx] = canvas()
  background(ctx, 'rgba(124,92,255,0.45)')

  circleImage(ctx, photo, 150, 170, 78, d.initials)
  text(ctx, d.playerName, 260, 160, { font: `800 {size}px ${SANS}`, size: 50, color: C.text, maxWidth: SHARE_W - 330 })
  text(ctx, `${d.period} in numbers`, 260, 215, { font: `600 {size}px ${SANS}`, size: 32, color: '#c9b8ff' })

  text(ctx, `${d.games} game${d.games === 1 ? '' : 's'}`, 72, 420, { font: `700 {size}px ${DISPLAY}`, size: 120, color: C.gold })
  text(ctx, `${d.wins} win${d.wins === 1 ? '' : 's'} · ${d.games - d.wins} loss${d.games - d.wins === 1 ? '' : 'es'}`, 72, 500, { font: `600 {size}px ${SANS}`, size: 44, color: C.muted })

  let y = 570
  for (const line of d.lines.slice(0, 5)) {
    roundRect(ctx, 72, y, SHARE_W - 144, 110, 28, 'rgba(255,255,255,0.05)', C.line)
    text(ctx, line.label.toUpperCase(), 112, y + 46, { font: `700 {size}px ${SANS}`, size: 26, color: C.faint })
    if (line.detail) text(ctx, line.detail, 112, y + 86, { font: `400 {size}px ${SANS}`, size: 30, color: C.muted, maxWidth: SHARE_W - 520 })
    text(ctx, line.value, SHARE_W - 112, y + 76, { font: `700 {size}px ${DISPLAY}`, size: 60, color: C.lime, align: 'right', maxWidth: 340 })
    y += 128
  }

  footer(ctx, 'StatsHub · season summary')
  return el.toDataURL('image/png')
}

// Shares the picture where the phone can (WhatsApp, Instagram…), or saves it.
export async function shareOrSave(dataUrl: string, fileName: string, title: string) {
  // (Not fetch(dataUrl) - the site's content policy doesn't let it fetch data: addresses.)
  const bytes = Uint8Array.from(atob(dataUrl.split(',')[1]), (c) => c.charCodeAt(0))
  const file = new File([bytes], fileName, { type: 'image/png' })
  if (navigator.canShare?.({ files: [file] })) {
    try {
      await navigator.share({ files: [file], title })
      return 'shared'
    } catch {
      return 'cancelled'
    }
  }
  save(dataUrl, fileName)
  return 'saved'
}

export function save(dataUrl: string, fileName: string) {
  const a = document.createElement('a')
  a.href = dataUrl
  a.download = fileName
  a.click()
}
