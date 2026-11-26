import { useMemo, useSyncExternalStore } from 'react'
import { toast } from 'sonner'
import { copyText } from './copy-text.ts'
import { normalize, rangeSize } from './selection.ts'
import type { SelectionStore } from './selection-store.ts'
import { formatTotal, selectionTotals } from './selection-totals.ts'

interface Item {
  label: string
  text: string
  raw: number
}

export function SelectionTotals({ store }: { store: SelectionStore }) {
  const { range, rows, columns } = useSyncExternalStore(store.subscribe, store.get)
  const totals = useMemo(
    () => (range ? selectionTotals(rows, columns, normalize(range)) : null),
    [rows, columns, range],
  )

  if (!range || !totals || rangeSize(range) < 2) return null

  const items: Item[] = [{ label: 'Count', text: totals.count.toLocaleString(), raw: totals.count }]
  const n = totals.numeric
  if (n && !totals.skipped) {
    items.push(
      { label: 'Sum', text: formatTotal(n.sum, n.decimals), raw: n.sum },
      {
        label: 'Average',
        text: formatTotal(n.avg, Math.min(4, Math.max(n.decimals, 2))),
        raw: n.avg,
      },
      { label: 'Min', text: formatTotal(n.min, n.decimals), raw: n.min },
      { label: 'Max', text: formatTotal(n.max, n.decimals), raw: n.max },
    )
  }

  const copy = (item: Item) => {
    if (copyText(String(item.raw))) toast(`Copied ${item.label}`)
    else toast.error('Copy failed.')
  }

  return (
    <div
      className="ml-auto flex flex-wrap items-center gap-x-3"
      title={totals.skipped ? 'Totals skipped for very large selections' : undefined}
    >
      {items.map((item) => (
        <button
          key={item.label}
          type="button"
          aria-label={`${item.label} ${item.text}, copy`}
          onClick={() => copy(item)}
          className="rounded-sm px-0.5 hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring"
        >
          {item.label} <span className="text-foreground">{item.text}</span>
        </button>
      ))}
    </div>
  )
}
