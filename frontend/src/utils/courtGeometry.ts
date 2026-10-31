// Half-court geometry, in a fixed viewBox. Kept in pixel units here and
// normalized to 0..1 only at the component boundary (props/callbacks),
// so the math for the 3PT arc stays simple ordinary-circle geometry.
const W = 500
const H = 470
const HOOP = { x: 250, y: 40 }
const ARC_R = 215
const CORNER_X_LEFT = 40
const CORNER_X_RIGHT = W - CORNER_X_LEFT
const CORNER_DX = HOOP.x - CORNER_X_LEFT
const CORNER_Y = HOOP.y + Math.sqrt(ARC_R * ARC_R - CORNER_DX * CORNER_DX)

export function classifyShotValue(px: number, py: number): 2 | 3 {
  const dx = px - HOOP.x
  const dy = py - HOOP.y
  if (py <= CORNER_Y) {
    return Math.abs(dx) > CORNER_DX ? 3 : 2
  }
  const dist = Math.sqrt(dx * dx + dy * dy)
  return dist > ARC_R ? 3 : 2
}

export const COURT = { W, H, HOOP, ARC_R, CORNER_X_LEFT, CORNER_X_RIGHT, CORNER_DX, CORNER_Y }
