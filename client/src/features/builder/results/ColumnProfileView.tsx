import { useMemo, useSyncExternalStore } from 'react'
import { Histogram } from './Histogram'
import { profileColumn, type Bin, type ColumnProfile } from './column-profile.ts'
import { formatTotal } from './selection-totals.ts'
import { TypeBadge } from './TypeBadge'
import type { SelectionStore } from './selection-store.ts'
import type { QueryResultDto } from '@/types'

interface Props {
  store: SelectionStore
  result: QueryResultDto
  rows: Record<string, unknown>[]
  filtered: boolean
}

const HINT = <p className="p-3 text-xs text-muted-foreground">Select a cell to profile its column.</p>

export function ColumnProfileView({ store, result, rows, filtered }: Props) {
  const { range, columns } = useSyncExternalStore(store.subscribe, store.get)
  const column = range ? columns[range.focus.col] : undefined
  // The store can still hold the previous run's columns for one frame after a re-run.
  if (!range || !column || !result.columns.includes(column)) return HINT
  return <Profile result={result} column={column} rows={rows} filtered={filtered} />
}

function Profile({
  result,
  column,
  rows,
  filtered,
}: {
  result: QueryResultDto
  column: QueryResultDto['columns'][number]
  rows: Record<string, unknown>[]
  filtered: boolean
}) {
  const profile = useMemo(() => profileColumn(rows, column), [rows, column])
  const n = rows.length
  const m = result.rows.length
  const withTime = column.dataType === 'dateTime' && !(profile.kind === 'date' && profile.allMidnight)
  const fmtDate = (ms: number) =>
    withTime ?new Date(ms).toISOString().replace('T', ' ').slice(0, 16) : new Date(ms).toISOString().slice(0, 10)
  const fmtNum = (v: number) => formatTotal(v, 4)

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="shrink-0 space-y-1.5 border-b border-border px-3 py-2">
        <div className="flex items-center gap-1.5">
          <span className="min-w-0 truncate text-sm font-semibold">{column.name}</span>
          <TypeBadge dataType={column.dataType} />
        </div>
        {result.truncated ? (
          <>
            <div
              role="status"
              className="flex items-center gap-2 border border-warning/40 bg-warning/20 px-2 py-1.5 text-xs text-foreground"
            >
              <span className="size-2.5 shrink-0 bg-warning" aria-hidden />
              <span>
                {filtered
                  ? `Based on ${n.toLocaleString()} filtered rows of the first ${m.toLocaleString()}. The query has more.`
                  : `Based on the first ${n.toLocaleString()} rows. The query has more.`}
              </span>
            </div>
            <button
              type="button"
              disabled
              className="w-full cursor-not-allowed border border-dashed border-border px-2 py-1 text-xs text-muted-foreground"
            >
              Calculate for all rows · coming later
            </button>
          </>
        ) : (
          <p className="text-xs text-muted-foreground">
            {filtered ? `Based on ${n.toLocaleString()} filtered rows` : `Based on all ${n.toLocaleString()} rows`}
          </p>
        )}
      </div>

      <div className="min-h-0 flex-1 space-y-3 overflow-y-auto px-3 py-3">
        <div className="space-y-1.5">
          <Meter label="Empty" count={profile.empty} total={profile.rows} showPercent />
          <Line label="Distinct" value={profile.distinct.toLocaleString()} />
          {profile.unreadable > 0 && <Line label="Unreadable" value={profile.unreadable.toLocaleString()} />}
        </div>
        <Body
          profile={profile}
          name={column.name}
          fmtNum={fmtNum}
          fmtDate={fmtDate}
        />
      </div>
    </div>
  )
}

function Body({
  profile,
  name,
  fmtNum,
  fmtDate,
}: {
  profile: ColumnProfile
  name: string
  fmtNum: (n: number) => string
  fmtDate: (ms: number) => string
}) {
  switch (profile.kind) {
    case 'number':
      return (
        <>
          <div className="grid grid-cols-3 gap-2">
            <Tile label="Min" value={opt(profile.min, fmtNum)} />
            <Tile label="Average" value={opt(profile.mean, fmtNum)} />
            <Tile label="Max" value={opt(profile.max, fmtNum)} />
            {profile.showSum && <Tile label="Sum" value={opt(profile.sum, fmtNum)} />}
            <Tile label="Median" value={opt(profile.median, fmtNum)} />
          </div>
          <Hist bins={profile.bins} name={name} fmt={fmtNum} />
        </>
      )
    case 'date':
      return (
        <>
          <div className="grid grid-cols-2 gap-2">
            <Tile label="Earliest" value={opt(profile.earliest, fmtDate)} />
            <Tile label="Latest" value={opt(profile.latest, fmtDate)} />
          </div>
          <Hist bins={profile.bins} name={name} fmt={fmtDate} />
        </>
      )
    case 'text':
      return (
        <>
          <div className="grid grid-cols-2 gap-2">
            <Tile label="Shortest" value={opt(profile.shortest, (v) => v.toLocaleString())} />
            <Tile label="Longest" value={opt(profile.longest, (v) => v.toLocaleString())} />
          </div>
          {profile.top.length > 0 && (
            <ul className="space-y-1">
              {profile.top.map((t) => (
                <li key={t.value} className="flex items-center gap-2 text-xs">
                  <span className="min-w-0 flex-1 truncate text-foreground" title={t.value}>
                    {t.value}
                  </span>
                  <div className="flex w-2/5 shrink-0">
                    <Bar fraction={profile.rows ? t.count / profile.rows : 0} />
                  </div>
                  <span className="font-numeric w-10 shrink-0 text-right tabular-nums text-foreground">
                    {t.count.toLocaleString()}
                  </span>
                </li>
              ))}
            </ul>
          )}
          {profile.other > 0 && (
            <p className="text-xs text-muted-foreground">Other values: {profile.other.toLocaleString()}</p>
          )}
        </>
      )
    case 'boolean':
      return (
        <div className="space-y-1">
          <Meter label="Yes" count={profile.yes} total={profile.rows} />
          <Meter label="No" count={profile.no} total={profile.rows} />
          <Meter label="Empty" count={profile.empty} total={profile.rows} />
        </div>
      )
  }
}

function opt(v: number | null, f: (n: number) => string) {
  return v === null ? '—' : f(v)
}

function Hist({ bins, name, fmt }: { bins: Bin[]; name: string; fmt: (n: number) => string }) {
  if (bins.length === 0) return null
  const peak = bins.reduce((a, b) => (b.count > a.count ? b : a))
  const total = bins.reduce((a, b) => a + b.count, 0)
  const label = `Histogram of ${name}, ${bins.length} bins, peak ${fmt(peak.from)}–${fmt(peak.to)} (${peak.count.toLocaleString()} rows)`
  return (
    <div className="space-y-1">
      <Histogram bins={bins} label={label} formatBound={fmt} />
      <div className="font-numeric flex justify-between text-[10px] tabular-nums text-muted-foreground">
        <span>{fmt(bins[0].from)}</span>
        <span>{total.toLocaleString()} values</span>
        <span>{fmt(bins[bins.length - 1].to)}</span>
      </div>
    </div>
  )
}

function Tile({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0 border border-border px-2 py-1.5">
      <div className="text-[10px] uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="font-numeric truncate text-sm font-semibold tabular-nums text-foreground" title={value}>
        {value}
      </div>
    </div>
  )
}

function Line({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between text-xs">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-numeric tabular-nums text-foreground">{value}</span>
    </div>
  )
}

function Bar({ fraction }: { fraction: number }) {
  return (
    <div className="h-2 min-w-0 flex-1 bg-muted" aria-hidden>
      <div className="h-full bg-primary/60" style={{ width: `${Math.min(1, fraction) * 100}%` }} />
    </div>
  )
}

function Meter({
  label,
  count,
  total,
  showPercent,
}: {
  label: string
  count: number
  total: number
  showPercent?: boolean
}) {
  const fraction = total ? count / total : 0
  return (
    <div className="flex items-center gap-2 text-xs">
      <span className="w-12 shrink-0 text-muted-foreground">{label}</span>
      <Bar fraction={fraction} />
      <span className="font-numeric shrink-0 text-right tabular-nums text-foreground">
        {count.toLocaleString()}
        {showPercent && ` · ${(fraction * 100).toFixed(fraction > 0 && fraction < 0.1 ? 1 : 0)}%`}
      </span>
    </div>
  )
}
