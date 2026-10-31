import { useRef } from 'react'
import { shotZones, zoneHeat } from '../utils/shotZones'
import { classifyShotValue, COURT } from '../utils/courtGeometry'

const { W, H, HOOP, ARC_R, CORNER_X_LEFT, CORNER_X_RIGHT, CORNER_Y } = COURT

export interface ChartShot {
  id?: string
  x: number // 0..1, fraction of court width from left sideline
  y: number // 0..1, fraction of court length from baseline
  made: boolean
}

interface CourtShotChartProps {
  shots: ChartShot[]
  interactive?: boolean
  pendingShot?: { x: number; y: number } | null
  onCourtTap?: (x: number, y: number, value: 2 | 3) => void
  onRemoveShot?: (id: string) => void
  showLegend?: boolean
  // 'zones': each area's made / tried as a colored bubble instead of each shot
  view?: 'shots' | 'zones'
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
  view = 'shots',
}: CourtShotChartProps) {
  const zones = view === 'zones' ? shotZones(shots) : []
  const most = Math.max(1, ...zones.map((z) => z.tried))
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

        {zones.map((z) => {
          const r = 16 + 18 * Math.sqrt(z.tried / most)
          return (
            <g key={z.key} className={`zone-bubble ${zoneHeat(z)}`}>
              <title>{`${z.label}: ${z.made} of ${z.tried}`}</title>
              <circle cx={z.x} cy={z.y} r={r} />
              <text x={z.x} y={z.y - 2} textAnchor="middle" className="zone-pct">{Math.round((z.made / z.tried) * 100)}%</text>
              <text x={z.x} y={z.y + 14} textAnchor="middle" className="zone-count">{z.made}/{z.tried}</text>
            </g>
          )
        })}

        {view === 'shots' && shots.map((shot, i) => {
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

      {showLegend && view === 'zones' && (
        <div className="court-legend">
          <span><i className="legend-dot zone-hot" /> 50%+</span>
          <span><i className="legend-dot zone-warm" /> 34-49%</span>
          <span><i className="legend-dot zone-cold" /> under 34%</span>
        </div>
      )}
      {showLegend && view === 'shots' && (
        <div className="court-legend">
          <span><i className="legend-dot made" /> Made</span>
          <span><i className="legend-dot missed" /> Missed</span>
          {interactive && <span className="legend-hint">Tap the court to log a shot</span>}
        </div>
      )}
    </div>
  )
}
