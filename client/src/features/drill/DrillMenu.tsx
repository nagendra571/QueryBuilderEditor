import { useRef, type KeyboardEvent, type RefObject } from 'react'
import { Popover, PopoverAnchor, PopoverContent } from '@/components/ui/popover'
import type { DrillMenuItem } from './drill-model'

const ITEM =
  'flex w-full items-center rounded-md px-2.5 py-1.5 text-left text-sm outline-none hover:bg-muted focus-visible:bg-muted focus-visible:ring-2 focus-visible:ring-ring'

export interface DrillMenuProps {
  /** Viewport coordinates of the click, or of the list item chosen from the keyboard. */
  anchor: { x: number; y: number }
  items: DrillMenuItem[]
  breakdownChoices: string[]
  rowCountHint?: number | null
  onRows(): void
  onBreakdown(column: string): void
  onFilter(): void
  onClose(): void
  /** Where focus goes when the menu closes; without it Radix falls back to whatever was focused before. */
  returnFocusRef?: RefObject<HTMLElement | null>
}

export function DrillMenu({
  anchor,
  items,
  breakdownChoices,
  rowCountHint,
  onRows,
  onBreakdown,
  onFilter,
  onClose,
  returnFocusRef,
}: DrillMenuProps) {
  const menu = useRef<HTMLDivElement>(null)
  const showBreakdown = items.includes('breakdown') && breakdownChoices.length > 0

  const move = (e: KeyboardEvent<HTMLDivElement>) => {
    const entries = Array.from(menu.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? [])
    if (entries.length === 0) return
    const at = entries.indexOf(document.activeElement as HTMLElement)
    let next = -1
    if (e.key === 'ArrowDown') next = (at + 1) % entries.length
    else if (e.key === 'ArrowUp') next = (at - 1 + entries.length) % entries.length
    else if (e.key === 'Home') next = 0
    else if (e.key === 'End') next = entries.length - 1
    if (next < 0) return
    e.preventDefault()
    entries[next].focus()
  }

  const run = (action: () => void) => () => {
    action()
    onClose()
  }

  return (
    <Popover open onOpenChange={(open) => !open && onClose()}>
      {/* A 1px box at the pointer: the popover then opens beside the mark that was clicked. */}
      <PopoverAnchor asChild>
        <span aria-hidden="true" className="pointer-events-none fixed size-px" style={{ left: anchor.x, top: anchor.y }} />
      </PopoverAnchor>
      <PopoverContent
        align="start"
        side="bottom"
        sideOffset={6}
        collisionPadding={8}
        className="w-60 gap-0.5 p-1"
        onOpenAutoFocus={(e) => {
          e.preventDefault()
          menu.current?.querySelector<HTMLElement>('[role="menuitem"]')?.focus()
        }}
        onCloseAutoFocus={(e) => {
          const target = returnFocusRef?.current
          if (target) {
            e.preventDefault()
            target.focus()
          }
        }}
      >
        <div ref={menu} role="menu" aria-label="Explore this value" onKeyDown={move} className="flex flex-col gap-0.5">
          {items.includes('rows') && (
            <button type="button" role="menuitem" className={ITEM} onClick={run(onRows)}>
              {rowCountHint != null ? `See the ${rowCountHint.toLocaleString()} rows` : 'See these rows'}
            </button>
          )}
          {showBreakdown && (
            <div role="group" aria-label="Break down by" className="flex flex-col gap-0.5">
              <p className="map-label px-2.5 pt-1.5 pb-0.5 text-muted-foreground">Break down by</p>
              {breakdownChoices.map((column) => (
                <button key={column} type="button" role="menuitem" title={column} className={`${ITEM} pl-4`} onClick={run(() => onBreakdown(column))}>
                  <span className="min-w-0 truncate">{column}</span>
                </button>
              ))}
            </div>
          )}
          {items.includes('filter') && (
            <button type="button" role="menuitem" className={ITEM} onClick={run(onFilter)}>
              Filter this dashboard
            </button>
          )}
        </div>
      </PopoverContent>
    </Popover>
  )
}
