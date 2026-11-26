import { useSyncExternalStore } from 'react'
import { rangeSize } from './selection.ts'
import type { SelectionStore } from './selection-store.ts'
import { SelectionTotals } from './SelectionTotals.tsx'
import { footerRange, selectedCellsText } from './footer-text.ts'

export function ResultsFooter({
  first,
  last,
  filtered,
  raw,
  pinned,
  store,
}: {
  first: number
  last: number
  filtered: number
  raw: number
  pinned: string[]
  store: SelectionStore
}) {
  const { range: selection } = useSyncExternalStore(store.subscribe, store.get)
  const selectedCells = selection ? rangeSize(selection) : 0
  const range = footerRange(first, last, filtered, raw)
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-0.5 border-t border-border bg-background px-(--space-row-x) py-1 font-numeric text-xs tracking-wide text-muted-foreground tabular-nums">
      {range && <span>{range}</span>}
      {pinned.length > 0 && <span>Pinned: {pinned.join(', ')}</span>}
      {selectedCells > 0 && <span>{selectedCellsText(selectedCells)}</span>}
      <SelectionTotals store={store} />
    </div>
  )
}
