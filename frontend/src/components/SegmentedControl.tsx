import type { ReactNode } from 'react'
import { useLayoutEffect, useRef, useState } from 'react'

interface SegmentedOption<T extends string | number> {
  value: T
  label: ReactNode
}

interface SegmentedControlProps<T extends string | number> {
  options: SegmentedOption<T>[]
  value: T
  onChange: (value: T) => void
  className?: string
}

export default function SegmentedControl<T extends string | number>({ options, value, onChange, className }: SegmentedControlProps<T>) {
  const containerRef = useRef<HTMLDivElement>(null)
  const [highlight, setHighlight] = useState({ left: 0, width: 0 })

  useLayoutEffect(() => {
    const reposition = () => {
      const container = containerRef.current
      if (!container) return
      const activeBtn = container.querySelector<HTMLButtonElement>(`button[data-value="${String(value)}"]`)
      if (activeBtn) setHighlight({ left: activeBtn.offsetLeft, width: activeBtn.offsetWidth })
    }
    reposition()
    window.addEventListener('resize', reposition)
    return () => window.removeEventListener('resize', reposition)
  }, [value, options])

  return (
    <div className={`segmented ${className ?? ''}`} ref={containerRef}>
      <span className="segmented-highlight" style={{ transform: `translateX(${highlight.left}px)`, width: `${highlight.width}px` }} />
      {options.map((opt) => (
        <button
          key={String(opt.value)}
          type="button"
          data-value={opt.value}
          className={value === opt.value ? 'active' : ''}
          onClick={() => onChange(opt.value)}
        >
          {opt.label}
        </button>
      ))}
    </div>
  )
}
