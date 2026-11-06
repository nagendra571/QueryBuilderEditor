import { toNumber } from '../../visualizations/chart-data.ts'

// Postgres numeric text can arrive as a string, which collation would order '1.5' before '1.25'.
export function compareCells(a: unknown, b: unknown, numeric = false): number {
  const aNull = a === null || a === undefined
  const bNull = b === null || b === undefined
  if (aNull && bNull) return 0
  if (aNull) return 1 // nulls always sort last
  if (bNull) return -1

  if (typeof a === 'number' && typeof b === 'number') return a - b
  if (typeof a === 'boolean' && typeof b === 'boolean') return a === b ? 0 : a ? 1 : -1
  if (numeric) {
    const x = toNumber(a)
    const y = toNumber(b)
    if (typeof x === 'number' && typeof y === 'number') return x - y
  }
  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' })
}
