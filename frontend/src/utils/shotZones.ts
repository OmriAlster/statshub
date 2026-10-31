import { classifyShotValue, COURT } from './courtGeometry'
import type { ChartShot } from '../components/CourtShotChart'

const { W, H, HOOP, CORNER_Y } = COURT

// The court in nine areas, each with the spot its bubble sits on - the shots
// grouped by where they were taken, for the zones view (a heat map).
export interface ShotZone { key: string; label: string; x: number; y: number; made: number; tried: number }

function zoneOf(px: number, py: number): Omit<ShotZone, 'made' | 'tried'> {
  const three = classifyShotValue(px, py) === 3
  if (three) {
    if (py <= CORNER_Y) return px < HOOP.x ? { key: 'lc3', label: 'Left corner 3', x: 40, y: 85 } : { key: 'rc3', label: 'Right corner 3', x: W - 40, y: 85 }
    if (px < 190) return { key: 'lw3', label: 'Left wing 3', x: 80, y: 320 }
    if (px > W - 190) return { key: 'rw3', label: 'Right wing 3', x: W - 80, y: 320 }
    return { key: 'top3', label: 'Top of the arc 3', x: HOOP.x, y: 420 }
  }
  if (px >= HOOP.x - 80 && px <= HOOP.x + 80 && py <= 190) return { key: 'paint', label: 'Paint', x: HOOP.x, y: 105 }
  if (px < HOOP.x - 80) return { key: 'lmid', label: 'Left mid-range', x: 115, y: 140 }
  if (px > HOOP.x + 80) return { key: 'rmid', label: 'Right mid-range', x: W - 115, y: 140 }
  return { key: 'tmid', label: 'Top of the key', x: HOOP.x, y: 222 }
}

export function shotZones(shots: ChartShot[]): ShotZone[] {
  const zones = new Map<string, ShotZone>()
  for (const s of shots) {
    const z = zoneOf(s.x * W, s.y * H)
    const zone = zones.get(z.key) ?? { ...z, made: 0, tried: 0 }
    zone.tried++
    if (s.made) zone.made++
    zones.set(z.key, zone)
  }
  return [...zones.values()]
}

// cold under a third, warm, hot from half
export const zoneHeat = (z: ShotZone) => (z.made / z.tried >= 0.5 ? 'hot' : z.made / z.tried >= 0.34 ? 'warm' : 'cold')
