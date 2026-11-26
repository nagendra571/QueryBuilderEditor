import type { GridView } from './grid-view.ts'

export function displayString(value: unknown): string {
  if (value === null || value === undefined) return ''
  if (typeof value === 'number') return value.toLocaleString()
  return String(value)
}

export function matchesSearch(value: unknown, term: string): boolean {
  const t = term.trim().toLowerCase()
  if (!t) return true
  return String(value ?? '').toLowerCase().includes(t)
}

// Left-pinned, then centre in `order`, then right-pinned; hidden and unknown names dropped.
export function displayOrder(view: Pick<GridView, 'order' | 'hidden' | 'pinnedLeft' | 'pinnedRight'>, names: string[]): string[] {
  const known = new Set(names)
  const hidden = new Set(view.hidden)
  const left = view.pinnedLeft.filter((n) => known.has(n) && !hidden.has(n))
  const right = view.pinnedRight.filter((n) => known.has(n) && !hidden.has(n))
  const pinned = new Set([...left, ...right])
  const centre = view.order.filter((n) => known.has(n) && !hidden.has(n) && !pinned.has(n))
  return [...left, ...centre, ...right]
}
