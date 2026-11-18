import type { ClickedMark } from './drill-model.ts'

export type MarkWithoutAnchor = Omit<ClickedMark, 'anchor'>

/** What a view needs to make its marks clickable; absent means the chart behaves exactly as before. */
export interface MarkDrill {
  onMark(mark: ClickedMark): void
  /** False for a mark the server could not drill by (a key past 2^53, a missing key): it must not look clickable. */
  canDrill(mark: MarkWithoutAnchor): boolean
  keyColumns?: string[]
}

interface PointerLike {
  clientX: number
  clientY: number
  detail?: number
  currentTarget?: unknown
}

/** A keyboard "click" (Enter on a button) reports 0,0; anchor the menu at the element instead. */
export function anchorOf(e: PointerLike): { x: number; y: number } {
  const target = e.currentTarget as { getBoundingClientRect?: () => DOMRect } | null | undefined
  if (e.detail === 0 && target?.getBoundingClientRect) {
    const r = target.getBoundingClientRect()
    return { x: r.left + Math.min(24, r.width / 2), y: r.bottom }
  }
  return { x: e.clientX, y: e.clientY }
}

export function fireMark(drill: MarkDrill | undefined, mark: MarkWithoutAnchor | null, e: PointerLike) {
  if (!drill || !mark || !drill.canDrill(mark)) return
  drill.onMark({ ...mark, anchor: anchorOf(e) })
}
