import { useMemo, useSyncExternalStore } from 'react'
import { toast } from 'sonner'
import { copyText } from './copy-text.ts'
import { normalize, rangeSize } from './selection.ts'
import type { SelectionStore } from './selection-store.ts'
import { formatTotal, selectionTotals } from './selection-totals.ts'

interface Item {
  label: string
  text: string
  copy: string
}

// Copy what the footer shows, without thousands separators or float noise like 4.4399999999999995.
const rounded = (v: number, d: number) => String(Number(v.toFixed(d)))

export function SelectionTotals({ store }: { store: SelectionStore }) {
  const { range, rows, columns } = useSyncExternalStore(store.subscribe, store.get)
  const totals = useMemo(
    () => (range ? selectionTotals(rows, columns, normalize(range)) : null),
    [rows, columns, range],
  )

  if (!range || !totals || rangeSize(range) < 2) return null

  const items: Item[] = [{ label: 'Count', text: totals.count.toLocaleString(), copy: String(totals.count) }]
  const n = totals.numeric
  if (n && !totals.skipped) {
    items.push(
      { label: 'Sum', text: formatTotal(n.sum, n.decimals), copy: rounded(n.sum, n.decimals) },
      {
        label: 'Average',
        text: formatTotal(n.avg, Math.min(4, Math.max(n.decimals, 2))),
        copy: rounded(n.avg, Math.min(4, Math.max(n.decimals, 2))),
      },
      { label: 'Min', text: formatTotal(n.min, n.decimals), copy: rounded(n.min, n.decimals) },
      { label: 'Max', text: formatTotal(n.max, n.decimals), copy: rounded(n.max, n.decimals) },
    )
  }

  const copy = (item: Item) => {
    if (copyText(item.copy)) toast(`Copied ${item.label}`)
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
