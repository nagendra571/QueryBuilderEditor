import { ArrowDown, ArrowUp, Plus, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { describeAggregate } from '@/features/builder/aggregate-functions'
import { useBuilderStore } from '@/features/builder/builder-store'
import type { ColumnMetadata, QueryColumn } from '@/types'

function useUnusedSortColumns(availableColumns: ColumnMetadata[]) {
  const sorts = useBuilderStore((s) => s.definition.sorts)
  return availableColumns.filter((c) => !sorts.some((s) => s.columnName === c.name))
}

function useUnusedMeasures() {
  const sorts = useBuilderStore((s) => s.definition.sorts)
  const columns = useBuilderStore((s) => s.definition.columns)
  return columns.filter(
    (c) => c.aggregate !== 'none' && !sorts.some((s) => s.tableAlias === c.tableAlias && s.columnName === c.columnName),
  )
}

export function AddSortControl({ availableColumns }: { availableColumns: ColumnMetadata[] }) {
  const sourceAlias = useBuilderStore((s) => s.definition.source.alias)
  const addSort = useBuilderStore((s) => s.addSort)
  const unusedColumns = useUnusedSortColumns(availableColumns)
  const unusedMeasures = useUnusedMeasures()

  if (unusedColumns.length === 0 && unusedMeasures.length === 0) return null

  return (
    <Select onValueChange={(value) => addSort(...(JSON.parse(value) as [string, string]))}>
      <SelectTrigger size="sm" className="h-7 text-xs">
        <Plus className="size-3.5" />
        <SelectValue placeholder="Add sort" />
      </SelectTrigger>
      <SelectContent>
        {unusedMeasures.map((m) => (
          <SelectItem key={`${m.tableAlias}.${m.columnName}`} value={JSON.stringify([m.tableAlias, m.columnName])} className="text-xs text-query">
            {describeAggregate(m.aggregate, m.alias || m.columnName)}
          </SelectItem>
        ))}
        {unusedColumns.map((col) => (
          <SelectItem key={col.name} value={JSON.stringify([sourceAlias, col.name])} className="text-xs">
            {col.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

export function SortPanel() {
  const sorts = useBuilderStore((s) => s.definition.sorts)
  const columns = useBuilderStore((s) => s.definition.columns)
  const updateSortDirection = useBuilderStore((s) => s.updateSortDirection)
  const removeSort = useBuilderStore((s) => s.removeSort)

  if (sorts.length === 0) {
    return (
      <div className="rounded-md border border-dashed border-border p-(--space-section) text-center text-xs text-muted-foreground">
        No sort order set. Rows come back in the database's natural order.
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-2">
      {sorts.map((sort, index) => {
        const measure = columns.find(
          (c: QueryColumn) => c.tableAlias === sort.tableAlias && c.columnName === sort.columnName && c.aggregate !== 'none',
        )
        const label = measure ? describeAggregate(measure.aggregate, measure.alias || measure.columnName) : sort.columnName
        return (
          <div key={`${sort.tableAlias}.${sort.columnName}`} className="flex items-center gap-1.5 rounded-md border border-border bg-card px-2 py-(--space-row-y)">
            <span className="w-40 truncate text-xs font-medium">{label}</span>
            <Button
              variant="outline"
              size="sm"
              className="h-7 gap-1 text-xs"
              onClick={() => updateSortDirection(index, sort.direction === 'asc' ? 'desc' : 'asc')}
            >
              {sort.direction === 'asc' ? <ArrowUp className="size-3.5" /> : <ArrowDown className="size-3.5" />}
              {sort.direction === 'asc' ? 'Ascending' : 'Descending'}
            </Button>
            <Button variant="ghost" size="icon" className="ml-auto size-6 text-destructive hover:text-destructive" onClick={() => removeSort(index)}>
              <X className="size-3.5" />
            </Button>
          </div>
        )
      })}
    </div>
  )
}
