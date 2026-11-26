import { Check, X as XIcon } from 'lucide-react'
import { displayString } from './grid-columns.ts'
import type { ColumnDataType } from '@/types'
import type { CellLook } from './grid-format.ts'

export function GridCell({
  value,
  dataType,
  wrap,
  look,
}: {
  value: unknown
  dataType: ColumnDataType
  wrap: boolean
  /** From the column's compiled format; absent renders the plain value. */
  look?: CellLook
}) {
  if (value === null || value === undefined) {
    return <span className="text-muted-foreground/50 italic">null</span>
  }
  if (typeof value === 'boolean') {
    return value ? (
      <Check className="size-4 text-success" aria-label="true" />
    ) : (
      <XIcon className="size-4 text-muted-foreground" aria-label="false" />
    )
  }
  const text = look ? look.text : displayString(value)
  const title = look?.title ?? text
  const bar = look?.bar
  if (wrap && dataType === 'text') {
    return (
      <span className="line-clamp-4 min-w-0 whitespace-pre-line break-words" title={title}>
        {text}
      </span>
    )
  }
  return (
    <>
      {bar && (
        <span
          aria-hidden
          className={`pointer-events-none absolute ${bar.negative ? 'bg-destructive/20' : 'bg-primary/20'}`}
          style={{ top: 6, bottom: 6, left: `${bar.from * 100}%`, width: `${(bar.to - bar.from) * 100}%` }}
        />
      )}
      <span className="relative block min-w-0 truncate" title={title}>
        {text}
      </span>
    </>
  )
}
