import type { MutableRefObject } from 'react'
import { usePlotArea, useXAxisScale } from 'recharts'
import { bandIndexAt, nearestPointIndex } from '@/features/visualizations/mark-sources'

export interface PlotGeometry {
  xs: (number | undefined)[]
  left: number
  right: number
  banded: boolean
  count: number
}

/** Renders nothing; copies the drawn points' x positions out of the chart so a click can be mapped without hover state. */
export function PlotGeometryReporter({ labels, into, banded = false }: { labels: unknown[]; into: MutableRefObject<PlotGeometry | null>; banded?: boolean }) {
  const scale = useXAxisScale()
  const area = usePlotArea()
  if (scale && area) {
    into.current = {
      xs: labels.map((l) => {
        const v = scale(l as never)
        return typeof v === 'number' ? v : undefined
      }),
      banded,
      count: labels.length,
      left: area.x,
      right: area.x + area.width,
    }
  }
  return null
}

/** Index of the point nearest a click on the chart wrapper, or undefined outside the plot. */
export function pointIndexAtClick(e: { clientX: number; currentTarget: unknown }, geometry: PlotGeometry | null): number | undefined {
  const svg = (e.currentTarget as HTMLElement | null)?.querySelector?.('svg.recharts-surface')
  if (!svg || !geometry) return undefined
  const x = e.clientX - svg.getBoundingClientRect().left
  if (geometry.banded) return bandIndexAt(x, geometry.left, geometry.right, geometry.count)
  return nearestPointIndex(geometry.xs, x, geometry.left, geometry.right)
}
