import type { QueryResultColumnDto } from '@/types'
import type { CellRange } from './selection.ts'

export interface GridSnapshot {
  range: CellRange | null
  rows: Record<string, unknown>[]
  columns: QueryResultColumnDto[]
}

export interface SelectionStore {
  get(): GridSnapshot
  publish(next: Partial<GridSnapshot>): void
  subscribe(listener: () => void): () => void
}

function sameRange(a: CellRange | null, b: CellRange | null) {
  if (a === b) return true
  if (!a || !b) return false
  return (
    a.anchor.row === b.anchor.row &&
    a.anchor.col === b.anchor.col &&
    a.focus.row === b.focus.row &&
    a.focus.col === b.focus.col
  )
}

// get() must return the same object until something really changes, or useSyncExternalStore loops.
export function createSelectionStore(): SelectionStore {
  let snapshot: GridSnapshot = { range: null, rows: [], columns: [] }
  const listeners = new Set<() => void>()
  return {
    get: () => snapshot,
    publish(next) {
      const changed =
        (next.range !== undefined && !sameRange(next.range, snapshot.range)) ||
        (next.rows !== undefined && next.rows !== snapshot.rows) ||
        (next.columns !== undefined && next.columns !== snapshot.columns)
      if (!changed) return
      snapshot = {
        range: next.range !== undefined && !sameRange(next.range, snapshot.range) ? next.range : snapshot.range,
        rows: next.rows ?? snapshot.rows,
        columns: next.columns ?? snapshot.columns,
      }
      listeners.forEach((l) => l())
    },
    subscribe(listener) {
      listeners.add(listener)
      return () => {
        listeners.delete(listener)
      }
    },
  }
}
