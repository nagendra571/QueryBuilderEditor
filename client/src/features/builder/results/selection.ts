export interface CellPos {
  row: number
  col: number
}
export interface CellRange {
  anchor: CellPos
  focus: CellPos
}
export interface GridBounds {
  rows: number
  cols: number
}
export type MoveKey =
  | 'up'
  | 'down'
  | 'left'
  | 'right'
  | 'home'
  | 'end'
  | 'pageUp'
  | 'pageDown'
  | 'ctrlHome'
  | 'ctrlEnd'

export function normalize(r: CellRange) {
  return {
    top: Math.min(r.anchor.row, r.focus.row),
    bottom: Math.max(r.anchor.row, r.focus.row),
    left: Math.min(r.anchor.col, r.focus.col),
    right: Math.max(r.anchor.col, r.focus.col),
  }
}

const clamp = (v: number, max: number) => Math.max(0, Math.min(v, max))

export function moveFocus(p: CellPos, key: MoveKey, b: GridBounds, pageRows: number): CellPos {
  if (b.rows <= 0 || b.cols <= 0) return { row: 0, col: 0 }
  const lastRow = b.rows - 1
  const lastCol = b.cols - 1
  let { row, col } = p
  switch (key) {
    case 'up': row -= 1; break
    case 'down': row += 1; break
    case 'left': col -= 1; break
    case 'right': col += 1; break
    case 'home': col = 0; break
    case 'end': col = lastCol; break
    case 'pageUp': row -= pageRows; break
    case 'pageDown': row += pageRows; break
    case 'ctrlHome': row = 0; col = 0; break
    case 'ctrlEnd': row = lastRow; col = lastCol; break
  }
  return { row: clamp(row, lastRow), col: clamp(col, lastCol) }
}

export function selectAll(b: GridBounds): CellRange | null {
  if (b.rows <= 0 || b.cols <= 0) return null
  return { anchor: { row: 0, col: 0 }, focus: { row: b.rows - 1, col: b.cols - 1 } }
}

export function clampRange(r: CellRange, b: GridBounds): CellRange | null {
  if (b.rows <= 0 || b.cols <= 0) return null
  const c = (p: CellPos): CellPos => ({ row: clamp(p.row, b.rows - 1), col: clamp(p.col, b.cols - 1) })
  return { anchor: c(r.anchor), focus: c(r.focus) }
}

export function rangeContains(r: CellRange, p: CellPos): boolean {
  const n = normalize(r)
  return p.row >= n.top && p.row <= n.bottom && p.col >= n.left && p.col <= n.right
}

export function rangeSize(r: CellRange): number {
  const n = normalize(r)
  return (n.bottom - n.top + 1) * (n.right - n.left + 1)
}
