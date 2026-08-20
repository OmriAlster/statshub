import { useRef } from 'react'

export interface ChartShot {
  id?: number
  x: number // 0..1, fraction of court width from left sideline
  y: number // 0..1, fraction of court length from baseline
  made: boolean
}

interface CourtShotChartProps {
  shots: ChartShot[]
  interactive?: boolean
  pendingShot?: { x: number; y: number } | null
  onCourtTap?: (x: number, y: number, value: 2 | 3) => void
  onRemoveShot?: (id: number) => void
  showLegend?: boolean
}

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

function arcPoints(steps = 48): string {
  const angleA = Math.atan2(CORNER_Y - HOOP.y, CORNER_X_LEFT - HOOP.x)
  const angleB = Math.atan2(CORNER_Y - HOOP.y, CORNER_X_RIGHT - HOOP.x)
  const pts: string[] = []
  for (let i = 0; i <= steps; i++) {
    const t = angleA + ((angleB - angleA) * i) / steps
    const x = HOOP.x + ARC_R * Math.cos(t)
    const y = HOOP.y + ARC_R * Math.sin(t)
    pts.push(`${x.toFixed(1)},${y.toFixed(1)}`)
  }
  return pts.join(' ')
}

const ARC_PATH_POINTS = arcPoints()

export default function CourtShotChart({
  shots,
  interactive = false,
  pendingShot = null,
  onCourtTap,
  onRemoveShot,
  showLegend = true,
}: CourtShotChartProps) {
  const svgRef = useRef<SVGSVGElement>(null)

  const handleClick = (e: React.MouseEvent<SVGSVGElement>) => {
    if (!interactive || !svgRef.current) return
    const svg = svgRef.current
    const point = svg.createSVGPoint()
    point.x = e.clientX
    point.y = e.clientY
    const ctm = svg.getScreenCTM()
    if (!ctm) return
    const loc = point.matrixTransform(ctm.inverse())
    if (loc.x < 0 || loc.x > W || loc.y < 0 || loc.y > H) return
    const value = classifyShotValue(loc.x, loc.y)
    onCourtTap?.(loc.x / W, loc.y / H, value)
  }

  return (
    <div className="court-chart-wrap">
      <svg
        ref={svgRef}
        viewBox={`0 0 ${W} ${H}`}
        className={`court-svg ${interactive ? 'interactive' : ''}`}
        onClick={handleClick}
      >
        <rect x={0} y={0} width={W} height={H} className="court-floor" />

        {/* Key / lane */}
        <rect x={HOOP.x - 80} y={0} width={160} height={190} className="court-line" />
        {/* Free-throw circle */}
        <circle cx={HOOP.x} cy={190} r={60} className="court-line" />
        {/* Restricted area */}
        <path
          d={`M ${HOOP.x - 40} 0 A 40 40 0 0 0 ${HOOP.x + 40} 0`}
          className="court-line"
        />
        {/* 3PT corner lines */}
        <line x1={CORNER_X_LEFT} y1={0} x2={CORNER_X_LEFT} y2={CORNER_Y} className="court-line" />
        <line x1={CORNER_X_RIGHT} y1={0} x2={CORNER_X_RIGHT} y2={CORNER_Y} className="court-line" />
        {/* 3PT arc */}
        <polyline points={ARC_PATH_POINTS} className="court-line" fill="none" />
        {/* Half-court line */}
        <line x1={0} y1={H} x2={W} y2={H} className="court-line" />
        {/* Backboard + hoop */}
        <line x1={HOOP.x - 30} y1={22} x2={HOOP.x + 30} y2={22} className="court-line backboard" />
        <circle cx={HOOP.x} cy={HOOP.y} r={9} className="court-hoop" />

        {shots.map((shot, i) => {
          const cx = shot.x * W
          const cy = shot.y * H
          return (
            <g
              key={shot.id ?? i}
              className={`shot-marker ${shot.made ? 'made' : 'missed'} ${interactive && shot.id ? 'removable' : ''}`}
              onClick={(e) => {
                if (interactive && shot.id && onRemoveShot) {
                  e.stopPropagation()
                  onRemoveShot(shot.id)
                }
              }}
            >
              {shot.made ? (
                <circle cx={cx} cy={cy} r={9} />
              ) : (
                <>
                  <line x1={cx - 7} y1={cy - 7} x2={cx + 7} y2={cy + 7} />
                  <line x1={cx - 7} y1={cy + 7} x2={cx + 7} y2={cy - 7} />
                </>
              )}
            </g>
          )
        })}

        {pendingShot && (
          <circle
            cx={pendingShot.x * W}
            cy={pendingShot.y * H}
            r={13}
            className="pending-shot-marker"
          />
        )}
      </svg>

      {showLegend && (
        <div className="court-legend">
          <span><i className="legend-dot made" /> Made</span>
          <span><i className="legend-dot missed" /> Missed</span>
          {interactive && <span className="legend-hint">Tap the court to log a shot</span>}
        </div>
      )}
    </div>
  )
}
