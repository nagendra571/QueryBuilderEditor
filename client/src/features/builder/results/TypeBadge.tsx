import { cn } from '@/lib/utils'
import type { ColumnDataType } from '@/types'
import { TYPE_BADGE } from './type-badge.ts'

export function TypeBadge({ dataType, className }: { dataType: ColumnDataType; className?: string }) {
  return (
    <span
      className={cn(
        'shrink-0 rounded-sm border border-border px-1 font-numeric text-[10px] font-semibold leading-4 text-muted-foreground',
        className,
      )}
    >
      {TYPE_BADGE[dataType]}
    </span>
  )
}
