import { footerRange, selectedCellsText } from './footer-text.ts'

export function ResultsFooter({
  first,
  last,
  filtered,
  raw,
  pinned,
  selectedCells,
}: {
  first: number
  last: number
  filtered: number
  raw: number
  pinned: string[]
  selectedCells: number
}) {
  const range = footerRange(first, last, filtered, raw)
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-0.5 border-t border-border bg-background px-(--space-row-x) py-1 font-numeric text-xs tracking-wide text-muted-foreground tabular-nums">
      {range && <span>{range}</span>}
      {pinned.length > 0 && <span>Pinned: {pinned.join(', ')}</span>}
      {selectedCells > 0 && <span>{selectedCellsText(selectedCells)}</span>}
    </div>
  )
}
