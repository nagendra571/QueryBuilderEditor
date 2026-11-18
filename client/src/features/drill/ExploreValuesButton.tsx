import { useRef, useState, type KeyboardEvent, type Ref } from 'react'
import { ListFilter } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import type { ClickedMark } from './drill-model'
import type { ExploreItem } from './explore-items'

/** The keyboard (and screen reader) path into a chart: every mark as a list item that opens the same menu a click does. */
export function ExploreValuesButton({
  items,
  more = 0,
  onSelect,
  buttonRef,
}: {
  items: ExploreItem[]
  /** Marks left out of the list because the chart has more than it can show. */
  more?: number
  onSelect(mark: ClickedMark): void
  buttonRef?: Ref<HTMLButtonElement>
}) {
  const [open, setOpen] = useState(false)
  const list = useRef<HTMLDivElement>(null)

  if (items.length === 0) return null

  const move = (e: KeyboardEvent<HTMLDivElement>) => {
    const entries = Array.from(list.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? [])
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

  const choose = (item: ExploreItem, el: HTMLElement) => {
    const r = el.getBoundingClientRect()
    setOpen(false)
    onSelect({ ...item.mark, anchor: { x: r.left + Math.min(24, r.width / 2), y: r.bottom } })
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button ref={buttonRef} type="button" variant="ghost" size="sm" className="h-7 gap-1.5 px-2 text-xs">
          <ListFilter className="size-3.5" aria-hidden="true" />
          Explore values
        </Button>
      </PopoverTrigger>
      <PopoverContent
        align="end"
        className="max-h-72 w-72 gap-0.5 overflow-auto p-1"
        onOpenAutoFocus={(e) => {
          e.preventDefault()
          list.current?.querySelector<HTMLElement>('[role="menuitem"]')?.focus()
        }}
        // The drill menu opens next and owns focus; handing it back to this button here would race it.
        onCloseAutoFocus={(e) => e.preventDefault()}
      >
        <div ref={list} role="menu" aria-label="Values in this chart" onKeyDown={move} className="flex flex-col gap-0.5">
          {items.map((item) => (
            <button
              key={item.id}
              type="button"
              role="menuitem"
              className="flex w-full items-center justify-between gap-3 rounded-md px-2.5 py-1.5 text-left text-sm outline-none hover:bg-muted focus-visible:bg-muted focus-visible:ring-2 focus-visible:ring-ring"
              onClick={(e) => choose(item, e.currentTarget)}
            >
              <span className="min-w-0 truncate" title={item.label}>
                {item.label}
              </span>
              {item.value && <span className="font-numeric shrink-0 tabular-nums text-muted-foreground">{item.value}</span>}
            </button>
          ))}
          {more > 0 && <p className="px-2.5 py-1.5 text-xs text-muted-foreground">{more.toLocaleString()} more not listed. Add filters to narrow the chart.</p>}
        </div>
      </PopoverContent>
    </Popover>
  )
}
