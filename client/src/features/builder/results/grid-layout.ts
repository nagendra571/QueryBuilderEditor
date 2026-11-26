import type { GridView } from './grid-view.ts'

export const ROW_NUM_WIDTH = 44

export type ColumnGroup = 'left' | 'centre' | 'right'

export interface ColumnLayout {
  name: string
  width: number
  group: ColumnGroup
  /** Start of the column in the scrolled content, row-number column included. */
  x: number
  stickyLeft?: number
  stickyRight?: number
  /** Last left-pinned or first right-pinned column: draws the ink edge. */
  edge: boolean
}

// `names` is display order (left-pinned, centre, right-pinned), as `displayOrder` returns it.
export function layoutColumns(
  names: string[],
  widths: number[],
  pinnedLeft: string[],
  pinnedRight: string[],
): ColumnLayout[] {
  const left = new Set(pinnedLeft)
  const right = new Set(pinnedRight)
  const groups = names.map<ColumnGroup>((n) => (left.has(n) ? 'left' : right.has(n) ? 'right' : 'centre'))
  const lastLeft = groups.lastIndexOf('left')
  const firstRight = groups.indexOf('right')

  const out: ColumnLayout[] = []
  let x = ROW_NUM_WIDTH
  for (let i = 0; i < names.length; i++) {
    out.push({
      name: names[i],
      width: widths[i],
      group: groups[i],
      x,
      stickyLeft: groups[i] === 'left' ? x : undefined,
      edge: i === lastLeft || i === firstRight,
    })
    x += widths[i]
  }
  let rightOffset = 0
  for (let i = names.length - 1; i >= 0; i--) {
    if (groups[i] !== 'right') continue
    out[i].stickyRight = rightOffset
    rightOffset += widths[i]
  }
  return out
}

// Reorders inside the dragged column's own group; a drop onto another group is ignored.
export function moveColumn(view: GridView, active: string, over: string): GridView {
  const groupOf = (n: string): 'pinnedLeft' | 'pinnedRight' | 'order' =>
    view.pinnedLeft.includes(n) ? 'pinnedLeft' : view.pinnedRight.includes(n) ? 'pinnedRight' : 'order'
  const group = groupOf(active)
  if (group !== groupOf(over)) return view
  const list = [...view[group]]
  const from = list.indexOf(active)
  const to = list.indexOf(over)
  if (from < 0 || to < 0 || from === to) return view
  list.splice(to, 0, ...list.splice(from, 1))
  return { ...view, [group]: list }
}

export function pinColumn(view: GridView, name: string, side: 'left' | 'right' | null): GridView {
  const pinnedLeft = view.pinnedLeft.filter((n) => n !== name)
  const pinnedRight = view.pinnedRight.filter((n) => n !== name)
  if (side === 'left') pinnedLeft.push(name)
  if (side === 'right') pinnedRight.unshift(name)
  return { ...view, pinnedLeft, pinnedRight }
}
