import { Columns3, RotateCcw, Search } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { DataTypeIcon } from '@/lib/data-type-icons'
import type { QueryResultColumnDto } from '@/types'

export function ResultsToolbar({
  search,
  onSearchChange,
  allColumns,
  hiddenColumns,
  onToggleColumn,
  isDefaultView,
  onReset,
  executionTimeMs,
}: {
  search: string
  onSearchChange: (value: string) => void
  allColumns: QueryResultColumnDto[]
  hiddenColumns: Set<string>
  onToggleColumn: (name: string) => void
  isDefaultView: boolean
  onReset: () => void
  executionTimeMs: number
}) {
  const visibleCount = allColumns.length - hiddenColumns.size

  return (
    <div className="flex flex-wrap items-center gap-2 border-b border-border px-(--space-row-x) py-1.5">
      <div className="relative w-56">
        <Search className="absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-muted-foreground" />
        <Input
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          placeholder="Search results…"
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
          {allColumns.map((col) => (
            <DropdownMenuCheckboxItem
              key={col.name}
              checked={!hiddenColumns.has(col.name)}
              onCheckedChange={() => onToggleColumn(col.name)}
              onSelect={(e) => e.preventDefault()}
              className="gap-1.5 text-xs"
            >
              <DataTypeIcon dataType={col.dataType} />
              {col.name}
            </DropdownMenuCheckboxItem>
          ))}
        </DropdownMenuContent>
      </DropdownMenu>

      {!isDefaultView && (
        <Tooltip>
          <TooltipTrigger asChild>
            <Button variant="ghost" size="icon" className="size-7" onClick={onReset} aria-label="Reset view">
              <RotateCcw className="size-3.5" />
            </Button>
          </TooltipTrigger>
          <TooltipContent>Reset columns, sort, search &amp; paging</TooltipContent>
        </Tooltip>
      )}

      <div className="ml-auto flex items-center gap-2 text-xs text-muted-foreground">
        <span>{executionTimeMs}ms</span>
      </div>
    </div>
  )
}
