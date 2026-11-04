export const GRID_COLS = 12
export const MIN_W = 2
export const MIN_H = 2
export const MAX_H = 24
export const ROW_HEIGHT = 40
export const GAP = 16
export const NARROW_BELOW = 768

export interface Box {
  x: number
  y: number
  width: number
  height: number
}

type Keyed = Box & { key: string }

const clamp = (n: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, n))

const overlaps = (a: Box, b: Box) =>
  a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height

/** Same rule the server applies on save: visit by (y, x); each tile drops to the lowest free row in its columns. */
function placeInOrder<T extends Keyed>(items: T[], fixed: T[] = []): T[] {
  const sorted = [...items].sort((a, b) => a.y - b.y || a.x - b.x)
  const placed: T[] = [...fixed]
  const out: T[] = []
  for (const item of sorted) {
    let y = 0
    for (;;) {
      const candidate = { ...item, y }
      const hit = placed.find((p) => overlaps(p, candidate))
      if (!hit) {
        placed.push(candidate)
        out.push(candidate)
        break
      }
      y = hit.y + hit.height
    }
  }
  return out
}

export function compactTiles<T extends Keyed>(tiles: T[]): T[] {
  return placeInOrder(tiles)
}

/** A keyboard edit: the edited tile keeps the spot it asked for, others make way, then everything floats up. */
export function applyBox<T extends Keyed>(tiles: T[], key: string, patch: Partial<Box>): T[] {
  const target = tiles.find((t) => t.key === key)
  if (!target) return tiles
  const width = clamp(patch.width ?? target.width, MIN_W, GRID_COLS)
  // An unpatched height is kept as-is: a one-row section sits below MIN_H and must not be inflated by a move.
  const height = patch.height === undefined ? target.height : clamp(patch.height, MIN_H, MAX_H)
  const x = clamp(patch.x ?? target.x, 0, GRID_COLS - width)
  const y = Math.max(0, patch.y ?? target.y)
  const moved = { ...target, x, y, width, height }
  const others = placeInOrder(tiles.filter((t) => t.key !== key), [moved])
  return compactTiles([moved, ...others])
}

/** Row that puts a tile past its nearest neighbour above/below in the same columns; null at the edge. */
export function neighbourRow<T extends Keyed>(tiles: T[], key: string, direction: 'up' | 'down'): number | null {
  const me = tiles.find((t) => t.key === key)
  if (!me) return null
  const sharing = tiles.filter((t) => t.key !== key && t.x < me.x + me.width && me.x < t.x + t.width)
  if (direction === 'up') {
    const above = sharing.filter((t) => t.y < me.y).sort((a, b) => b.y - a.y)[0]
    return above ? above.y : null
  }
  const below = sharing.filter((t) => t.y >= me.y + me.height).sort((a, b) => a.y - b.y)[0]
  return below ? below.y + below.height : null
}

export function bottomRow(tiles: Box[]): number {
  return tiles.reduce((max, t) => Math.max(max, t.y + t.height), 0)
}

export function pixelsForRows(h: number): number {
  return h * ROW_HEIGHT + (h - 1) * GAP
}

/** Rows needed to hold a content height, plus the tile's own padding and border. */
export function rowsForContent(contentPx: number): number {
  const chrome = 26
  return clamp(Math.ceil((contentPx + chrome + GAP) / (ROW_HEIGHT + GAP)), MIN_H, MAX_H)
}

export function readingOrder<T extends Box>(tiles: T[]): T[] {
  return [...tiles].sort((a, b) => a.y - b.y || a.x - b.x)
}
