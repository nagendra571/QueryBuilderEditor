import type { QueryResultColumnDto } from '@/types'
import { toNumber } from '../../visualizations/chart-data.ts'
import { profileKind } from './column-profile.ts'

export const MAX_TOTAL_CELLS = 1_000_000

export interface SelectionTotals {
  cells: number
  count: number
  numeric: { sum: number; avg: number; min: number; max: number; decimals: number } | null
  skipped: boolean
}

export function decimalsOf(n: number): number {
  const s = String(n)
  if (s.includes('e')) return 0
  const dot = s.indexOf('.')
  return dot < 0 ? 0 : s.length - dot - 1
}

export function formatTotal(n: number, decimals: number): string {
  return n.toLocaleString(undefined, { maximumFractionDigits: decimals })
}

export function selectionTotals(
  rows: Record<string, unknown>[],
  columns: QueryResultColumnDto[],
  rect: { top: number; bottom: number; left: number; right: number },
): SelectionTotals {
  const bottom = Math.min(rect.bottom, rows.length - 1)
  const right = Math.min(rect.right, columns.length - 1)
  const height = Math.max(0, rect.bottom - rect.top + 1)
  const width = Math.max(0, rect.right - rect.left + 1)
  const cells = height * width
  const skipped = cells > MAX_TOTAL_CELLS

  let count = 0
  let n = 0
  let sum = 0
  let min = Infinity
  let max = -Infinity
  let decimals = 0

  for (let c = rect.left; c <= right; c++) {
    const col = columns[c]
    const numeric = !skipped && profileKind(col.dataType) === 'number'
    for (let r = rect.top; r <= bottom; r++) {
      const v = rows[r]?.[col.name]
      if (v === null || v === undefined || v === '') continue
      count++
      if (!numeric) continue
      const num = toNumber(v)
      if (typeof num !== 'number') continue
      n++
      sum += num
      if (num < min) min = num
      if (num > max) max = num
      decimals = Math.max(decimals, decimalsOf(num))
    }
  }

  return {
    cells,
    count,
    numeric: n === 0 ? null : { sum, avg: sum / n, min, max, decimals },
    skipped,
  }
}
