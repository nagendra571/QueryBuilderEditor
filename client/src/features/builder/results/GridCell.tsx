import { Check, X as XIcon } from 'lucide-react'
import { displayString } from './grid-columns.ts'
import type { ColumnDataType } from '@/types'

export function GridCell({ value, dataType, wrap }: { value: unknown; dataType: ColumnDataType; wrap: boolean }) {
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
  const text = displayString(value)
  if (wrap && dataType === 'text') {
    return (
      <span className="line-clamp-4 min-w-0 whitespace-normal break-words" title={text}>
        {text}
      </span>
    )
  }
  return (
    <span className="block min-w-0 truncate" title={text}>
      {text}
    </span>
  )
}
