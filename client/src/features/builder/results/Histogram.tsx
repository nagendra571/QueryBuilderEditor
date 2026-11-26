import type { Bin } from './column-profile.ts'

interface Props {
  bins: Bin[]
  label: string
  formatBound: (n: number) => string
}

export function Histogram({ bins, label, formatBound }: Props) {
  const maxCount = bins.reduce((m, b) => Math.max(m, b.count), 0)
  return (
    <div role="img" aria-label={label} className="flex h-16 items-end gap-px border-b border-border">
      {bins.map((b, i) => (
        <div
          key={i}
          title={`${formatBound(b.from)}–${formatBound(b.to)}: ${b.count.toLocaleString()} rows`}
          className="min-w-px flex-1 bg-primary/60"
          style={{ height: maxCount === 0 ? 0 : `${(b.count / maxCount) * 100}%` }}
        />
      ))}
    </div>
  )
}
