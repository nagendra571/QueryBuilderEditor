import type { Bin } from './column-profile.ts'

interface Props {
  bins: Bin[]
  label: string
  formatBound: (n: number) => string
  /** Makes each bar a button. */
  onBarClick?: (bin: Bin) => void
}

export function Histogram({ bins, label, formatBound, onBarClick }: Props) {
  const maxCount = bins.reduce((m, b) => Math.max(m, b.count), 0)
  const height = (b: Bin) => (maxCount === 0 ? 0 : `${(b.count / maxCount) * 100}%`)
  const tip = (b: Bin) => `${formatBound(b.from)}–${formatBound(b.to)}: ${b.count.toLocaleString()} rows`

  if (onBarClick) {
    return (
      <div role="group" aria-label={label} className="flex h-16 items-end gap-px border-b border-border">
        {bins.map((b, i) =>
          b.count === 0 ? (
            <div key={i} title={tip(b)} className="h-full min-w-px flex-1" />
          ) : (
          <button
            key={i}
            type="button"
            title={`${tip(b)} · Show only this range`}
            aria-label={`Show only this range: ${tip(b)}`}
            onClick={() => onBarClick(b)}
            className="flex h-full min-w-px flex-1 items-end focus-visible:outline-2 focus-visible:outline-ring"
          >
            <span className="block w-full bg-primary/60 hover:bg-primary" style={{ height: height(b) }} />
          </button>
          ),
        )}
      </div>
    )
  }
  return (
    <div role="img" aria-label={label} className="flex h-16 items-end gap-px border-b border-border">
      {bins.map((b, i) => (
        <div key={i} title={tip(b)} className="min-w-px flex-1 bg-primary/60" style={{ height: height(b) }} />
      ))}
    </div>
  )
}
