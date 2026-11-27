import { Columns3, Copy, LayoutTemplate, Maximize2, Minimize2, PanelRight, RotateCcw, Search, WrapText } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { useState } from 'react'
import { DataTypeIcon } from '@/lib/data-type-icons'
import { cn } from '@/lib/utils'
import type { QueryResultColumnDto } from '@/types'
import type { Density } from '@/state/density-store'

export interface LayoutMenuProps {
  /** Something to save: the view differs from the saved layout (and isn't just the plain grid with nothing saved). */
  dirty: boolean
  hasSaved: boolean
  saving: boolean
  onSave: () => void
  onClear: () => void
}

function LayoutMenu({ dirty, hasSaved, saving, onSave, onClear }: LayoutMenuProps) {
  const [confirmOpen, setConfirmOpen] = useState(false)
  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="outline" size="sm" className="h-7 gap-1.5 text-xs">
            <LayoutTemplate className="size-3.5" />
            Layout
            {dirty && <span className="size-1.5 rounded-full bg-primary" aria-label="Unsaved layout changes" />}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="start">
          <DropdownMenuItem className="text-xs" disabled={!dirty || saving} onSelect={onSave}>
            Save layout
          </DropdownMenuItem>
          <DropdownMenuItem className="text-xs" disabled={!hasSaved || saving} onSelect={() => setConfirmOpen(true)}>
            Clear saved layout
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
      <Dialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <DialogContent className="sm:max-w-sm">
          <DialogHeader>
            <DialogTitle>Clear the saved layout?</DialogTitle>
            <DialogDescription>
              Everyone will see the plain grid again. Your current view stays until you reset it.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={() => {
                setConfirmOpen(false)
                onClear()
              }}
            >
              Clear layout
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}

const DENSITIES: { value: Density; label: string }[] = [
  { value: 'comfortable', label: 'Comfortable' },
  { value: 'compact', label: 'Compact' },
]

function Meta({ label, value }: { label: string; value: string }) {
  return (
    <span className="font-numeric text-xs">
      <span className="tracking-widest text-muted-foreground uppercase">{label}</span>{' '}
      <span className="tabular-nums text-foreground">{value}</span>
    </span>
  )
}

export function ResultsToolbar({
  search,
  onSearchChange,
  searchCount,
  allColumns,
  hiddenColumns,
  onToggleColumn,
  density,
  onDensityChange,
  wrap,
  onWrapChange,
  detailsOpen,
  onToggleDetails,
  detailsRef,
  totalsOn,
  onTotalsChange,
  onCopy,
  copyDisabled,
  isDefaultView,
  onReset,
  rowCount,
  executionTimeMs,
  fullScreen,
  onToggleFullScreen,
  fullScreenRef,
  layoutMenu,
}: {
  search: string
  onSearchChange: (value: string) => void
  searchCount: number
  allColumns: QueryResultColumnDto[]
  hiddenColumns: string[]
  onToggleColumn: (name: string) => void
  density: Density
  onDensityChange: (d: Density) => void
  wrap: boolean
  onWrapChange: (wrap: boolean) => void
  detailsOpen: boolean
  onToggleDetails: () => void
  detailsRef?: React.Ref<HTMLButtonElement>
  totalsOn: boolean
  onTotalsChange: (on: boolean) => void
  onCopy: () => void
  copyDisabled: boolean
  isDefaultView: boolean
  onReset: () => void
  rowCount: number
  executionTimeMs: number
  fullScreen: boolean
  onToggleFullScreen: () => void
  fullScreenRef: React.Ref<HTMLButtonElement>
  /** Only passed for people who may save a layout with the query. */
  layoutMenu?: LayoutMenuProps
}) {
  const visibleCount = allColumns.length - hiddenColumns.length

  return (
    <div className="flex flex-wrap items-center gap-2 border-b border-border px-(--space-row-x) py-1.5">
      <div className="relative w-56">
        <Search className="absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-muted-foreground" />
        <Input
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          placeholder={`Search ${searchCount.toLocaleString()} rows…`}
          aria-label="Search rows"
          className="h-7 pl-8 text-xs"
        />
      </div>

      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="outline" size="sm" className="h-7 gap-1.5 text-xs">
            <Columns3 className="size-3.5" />
            Columns
            <Badge variant="secondary" className="h-4 px-1 text-[10px]">
              {visibleCount}/{allColumns.length}
            </Badge>
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="start" className="max-h-72 overflow-y-auto">
          <DropdownMenuLabel className="text-xs">Show columns</DropdownMenuLabel>
          <DropdownMenuSeparator />
          {allColumns.map((col) => {
            const shown = !hiddenColumns.includes(col.name)
            return (
              <DropdownMenuCheckboxItem
                key={col.name}
                checked={shown}
                disabled={shown && visibleCount <= 1}
                onCheckedChange={() => onToggleColumn(col.name)}
                onSelect={(e) => e.preventDefault()}
                className="gap-1.5 text-xs"
              >
                <DataTypeIcon dataType={col.dataType} />
                {col.name}
              </DropdownMenuCheckboxItem>
            )
          })}
        </DropdownMenuContent>
      </DropdownMenu>

      <span className="h-5 w-px bg-border" aria-hidden />

      <div role="radiogroup" aria-label="Row density" className="flex overflow-hidden rounded-md border border-border">
        {DENSITIES.map((d) => (
          <button
            key={d.value}
            type="button"
            role="radio"
            aria-checked={density === d.value}
            onClick={() => onDensityChange(d.value)}
            className={cn(
              'h-7 px-2 text-xs focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring',
              density === d.value ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:bg-muted',
            )}
          >
            {d.label}
          </button>
        ))}
      </div>

      <Button
        variant={wrap ? 'secondary' : 'outline'}
        size="sm"
        className="h-7 gap-1.5 text-xs"
        aria-pressed={wrap}
        onClick={() => onWrapChange(!wrap)}
      >
        <WrapText className="size-3.5" />
        Wrap text
      </Button>

      <Button
        variant={totalsOn ? 'secondary' : 'outline'}
        size="sm"
        className="h-7 gap-1.5 text-xs"
        aria-pressed={totalsOn}
        onClick={() => onTotalsChange(!totalsOn)}
      >
        <span aria-hidden className="font-numeric text-sm leading-none">Σ</span>
        Totals
      </Button>

      <Button
        ref={detailsRef}
        variant={detailsOpen ? 'secondary' : 'outline'}
        size="sm"
        className="h-7 gap-1.5 text-xs"
        aria-pressed={detailsOpen}
        onClick={onToggleDetails}
      >
        <PanelRight className="size-3.5" />
        Details
      </Button>

      <span className="h-5 w-px bg-border" aria-hidden />

      <Button variant="outline" size="sm" className="h-7 gap-1.5 text-xs" onClick={onCopy} disabled={copyDisabled}>
        <Copy className="size-3.5" />
        Copy
      </Button>

      {layoutMenu && <LayoutMenu {...layoutMenu} />}

      {!isDefaultView && (
        <Tooltip>
          <TooltipTrigger asChild>
            <Button variant="ghost" size="icon" className="size-7" onClick={onReset} aria-label="Reset view">
              <RotateCcw className="size-3.5" />
            </Button>
          </TooltipTrigger>
          <TooltipContent>Reset columns, sort, search and layout</TooltipContent>
        </Tooltip>
      )}

      <div className="ml-auto flex items-center gap-3 text-muted-foreground">
        <Meta label="Rows" value={rowCount.toLocaleString()} />
        <Meta label="Cols" value={String(visibleCount)} />
        <Meta label="Ran in" value={`${executionTimeMs.toLocaleString()} ms`} />
        {/* No Radix Tooltip here: this button is focused programmatically on entering and leaving full screen,
            and a tooltip opened by that focus swallowed the first Escape. */}
        <Button
          ref={fullScreenRef}
          variant="ghost"
          size="icon"
          className="size-7"
          aria-label={fullScreen ? 'Exit full screen' : 'Full screen'}
          title={fullScreen ? 'Exit full screen' : 'Full screen'}
          aria-pressed={fullScreen}
          onClick={onToggleFullScreen}
        >
          {fullScreen ? <Minimize2 className="size-3.5" /> : <Maximize2 className="size-3.5" />}
        </Button>
      </div>
    </div>
  )
}
