import type { CellRange } from './selection.ts'

/** Which view an entry belongs to: the joined search/sort/order/visibility/pinning key and the run's rows. */
export interface ViewKey {
  resetKey: string
  data: unknown
}

export interface SelectionEntry extends ViewKey {
  range: CellRange | null
}

export interface ScrollEntry extends ViewKey {
  top: number
  left: number
}

// Full screen moves the grid into a portal, which remounts it; this outlives the grid so the user comes back to
// the same rows, columns and selection. Entries carry their view key, so anything that would have cleared them
// in a mounted grid still clears them across the remount.
export interface GridMemory {
  readonly selection: SelectionEntry | null
  readonly scroll: ScrollEntry | null
  saveSelection: (entry: SelectionEntry) => void
  saveScroll: (entry: ScrollEntry) => void
}

export function createGridMemory(): GridMemory {
  let selection: SelectionEntry | null = null
  let scroll: ScrollEntry | null = null
  return {
    get selection() {
      return selection
    },
    get scroll() {
      return scroll
    },
    saveSelection: (entry) => {
      selection = entry
    },
    saveScroll: (entry) => {
      scroll = entry
    },
  }
}

export function sameView(entry: ViewKey | null | undefined, resetKey: string, data: unknown): boolean {
  return !!entry && entry.resetKey === resetKey && entry.data === data
}

export function restoredSelection(memory: GridMemory | undefined, resetKey: string, data: unknown): SelectionEntry {
  const saved = memory?.selection
  return saved && sameView(saved, resetKey, data) ? saved : { resetKey, data, range: null }
}

export function restoredScroll(
  memory: GridMemory | undefined,
  resetKey: string,
  data: unknown,
): { top: number; left: number } {
  const saved = memory?.scroll
  return saved && sameView(saved, resetKey, data) ? { top: saved.top, left: saved.left } : { top: 0, left: 0 }
}
