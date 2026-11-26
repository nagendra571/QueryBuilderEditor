import { memo, useMemo } from 'react'
import { ChevronDown } from 'lucide-react'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { cn } from '@/lib/utils'
import type { QueryResultColumnDto } from '@/types'
import type { CompiledFormat } from './grid-format.ts'
import { ROW_NUM_WIDTH, type ColumnLayout, type ColumnWindow } from './grid-layout.ts'
import { TOTAL_LABEL, defaultTotal, totalChoices, totalValue, type TotalFn } from './grid-totals.ts'

const CHOICE_NAME: Record<TotalFn, string> = {
  sum: 'Sum',
  avg: 'Average',
  count: 'Count',
  distinct: 'Distinct count',
  min: 'Minimum',
  max: 'Maximum',
  earliest: 'Earliest',
  latest: 'Latest',
  none: 'None',
}

const STICKY_BG = 'bg-muted'

export interface TotalsRowProps {
  /** The displayed rows (search and filters applied), as plain objects. */
  rows: readonly Record<string, unknown>[]
  columns: QueryResultColumnDto[]
  layouts: ColumnLayout[]
  colWindow: ColumnWindow
  /** Compiled format per column, in `columns` order. */
  formats: CompiledFormat[]
  fns: Record<string, TotalFn>
  height: number
  width: number
  rowCount: number
  onChange: (column: string, fn: TotalFn) => void
  /** Where focus goes when a menu closes: the totals buttons are not tab stops. */
  onFocusGrid: () => void
}

// Lives after the virtual sizer in the scroll container, so its height is part of the scrollable content and the
// last data row can scroll clear of it; sticky keeps it at the bottom of the viewport.
export const TotalsRow = memo(function TotalsRow({
  rows,
  columns,
  layouts,
  colWindow,
  formats,
  fns,
  height,
  width,
  rowCount,
  onChange,
  onFocusGrid,
}: TotalsRowProps) {
  const effective = useMemo(() => columns.map((c) => fns[c.name] ?? defaultTotal(c)), [columns, fns])
  const values = useMemo(
    () => columns.map((c, i) => totalValue(rows, c, effective[i], formats[i].text)),
    [rows, columns, effective, formats],
  )

  const cell = (ci: number) => {
    const col = columns[ci]
    const l = layouts[ci]
    const pinned = l.group !== 'centre'
    const fn = effective[ci]
    const isNumber = col.dataType === 'number'
    const choices = totalChoices(col)
    return (
      <div
        key={col.name}
        role="gridcell"
        aria-colindex={ci + 2}
        className={cn(
          'flex shrink-0 items-center self-stretch overflow-hidden border-r border-border/60 text-(length:--text-body)',
          pinned ? cn('sticky z-10', STICKY_BG) : 'relative',
        )}
        style={{
          width: l.width,
          ...(pinned ? { left: l.stickyLeft, right: l.stickyRight } : {}),
          ...(l.edge ? { boxShadow: `inset ${l.group === 'left' ? '-' : ''}2px 0 0 var(--foreground)` } : {}),
        }}
      >
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button
              type="button"
              tabIndex={-1}
              aria-haspopup="menu"
              aria-label={
                fn === 'none' ? `${col.name} total: none. Change total` : `${col.name} total: ${CHOICE_NAME[fn]} ${values[ci]}. Change total`
              }
              className={cn(
                'group/total flex h-full min-w-0 flex-1 items-center gap-1.5 px-2.5 hover:bg-foreground/5 focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring',
                isNumber && 'justify-end',
              )}
            >
              {fn === 'none' ? (
                <ChevronDown className="size-3 text-muted-foreground opacity-0 group-hover/total:opacity-100" aria-hidden />
              ) : (
                <>
                  <span className="shrink-0 font-numeric text-[10px] tracking-widest text-muted-foreground uppercase">
                    {TOTAL_LABEL[fn]}
                  </span>
                  <span className={cn('min-w-0 truncate font-numeric font-semibold tabular-nums', isNumber && 'text-right')}>
                    {values[ci]}
                  </span>
                </>
              )}
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent
            align="start"
            className="min-w-40"
            onCloseAutoFocus={(e) => {
              e.preventDefault()
              onFocusGrid()
            }}
          >
            <DropdownMenuRadioGroup value={fn} onValueChange={(v) => onChange(col.name, v as TotalFn)}>
              {choices.map((c) => (
                <DropdownMenuRadioItem key={c} value={c} className="text-xs">
                  {CHOICE_NAME[c]}
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    )
  }

  return (
    // Keys pressed here (and in the portalled menu, whose events bubble through React) must not drive the grid.
    // Escape still passes so full screen can exit.
    <div
      role="row"
      aria-rowindex={rowCount + 2}
      onKeyDown={(e) => {
        if (e.key !== 'Escape') e.stopPropagation()
      }}
      className={cn('sticky bottom-0 z-20 flex border-t-2 border-foreground', STICKY_BG)}
      style={{ width, minWidth: '100%', height }}
    >
      <div
        role="rowheader"
        aria-colindex={1}
        className={cn(
          'sticky left-0 z-10 flex shrink-0 items-center justify-end self-stretch border-r border-border/60 px-2.5 font-numeric text-xs font-semibold',
          STICKY_BG,
        )}
        style={{ width: ROW_NUM_WIDTH }}
      >
        Σ
      </div>
      {colWindow.left.map(cell)}
      {colWindow.before > 0 && <div aria-hidden className="shrink-0" style={{ width: colWindow.before }} />}
      {colWindow.centre.map(cell)}
      {colWindow.after > 0 && <div aria-hidden className="shrink-0" style={{ width: colWindow.after }} />}
      <div aria-hidden className="min-w-0 flex-1" />
      {colWindow.right.map(cell)}
    </div>
  )
})
