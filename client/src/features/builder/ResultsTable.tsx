import {
  DndContext,
  type DragEndEvent,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
} from '@dnd-kit/core'
import { restrictToHorizontalAxis, restrictToParentElement } from '@dnd-kit/modifiers'
import {
  SortableContext,
  horizontalListSortingStrategy,
  sortableKeyboardCoordinates,
  useSortable,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { AlertTriangle, ArrowDown, ArrowUp, Check, ChevronsUpDown, GripVertical, X as XIcon } from 'lucide-react'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { ResultsPagination } from '@/features/builder/results/ResultsPagination'
import { ResultsToolbar } from '@/features/builder/results/ResultsToolbar'
import { useResultsView } from '@/features/builder/results/useResultsView'
import { DataTypeIcon } from '@/lib/data-type-icons'
import { cn } from '@/lib/utils'
import type { QueryResultColumnDto, QueryResultDto } from '@/types'

export function ResultsTable({ result }: { result: QueryResultDto }) {
  const view = useResultsView(result)

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )

  function handleDragEnd(event: DragEndEvent) {
    const { active, over } = event
    if (!over || active.id === over.id) return
    view.reorderColumn(String(active.id), String(over.id))
  }

  if (result.columns.length === 0) {
    return <p className="p-(--space-section) text-center text-sm text-muted-foreground">Query returned no columns.</p>
  }

  return (
    <div className="flex h-full flex-col">
      <ResultsToolbar
        search={view.search}
        onSearchChange={view.setSearch}
        allColumns={view.allColumns}
        hiddenColumns={view.hiddenColumns}
        onToggleColumn={view.toggleColumnVisible}
        isDefaultView={view.isDefaultView}
        onReset={view.resetView}
        executionTimeMs={result.executionTimeMs}
      />

      {result.truncated && (
        <Alert className="mx-(--space-section) mt-2 w-auto border-warning/40 text-warning">
          <AlertTriangle className="size-4" />
          <AlertDescription className="text-warning">
            This query returns more than {result.rowLimit.toLocaleString()} records. Only the first{' '}
            {result.rowLimit.toLocaleString()} are shown. Add filters to narrow down the results.
          </AlertDescription>
        </Alert>
      )}

      {view.totalFiltered === 0 ? (
        <Alert className="m-(--space-section) w-auto">
          <AlertDescription>
            {view.totalRaw === 0 ? 'No rows matched your filters. Try widening a condition.' : 'No rows match your search.'}
          </AlertDescription>
        </Alert>
      ) : (
        <div className="relative min-h-0 flex-1">
          <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            onDragEnd={handleDragEnd}
            modifiers={[restrictToHorizontalAxis, restrictToParentElement]}
          >
            <Table>
              <TableHeader className="sticky top-0 z-10 bg-background">
                <TableRow>
                  <TableHead className="h-(--row-h) w-10 text-right whitespace-nowrap text-muted-foreground/60">#</TableHead>
                  <SortableContext items={view.visibleColumns.map((c) => c.name)} strategy={horizontalListSortingStrategy}>
                    {view.visibleColumns.map((col) => (
                      <SortableHeader
                        key={col.name}
                        column={col}
                        sortDirection={view.sort?.column === col.name ? view.sort.direction : null}
                        onSort={() => view.toggleSort(col.name)}
                      />
                    ))}
                  </SortableContext>
                </TableRow>
              </TableHeader>
              <TableBody>
                {view.pagedRows.map((row, i) => (
                  <TableRow key={view.rowNumberOffset + i}>
                    <TableCell className="h-(--row-h) text-right text-(length:--text-body) text-muted-foreground/60 tabular-nums">
                      {view.rowNumberOffset + i + 1}
                    </TableCell>
                    {view.visibleColumns.map((col) => (
                      <TableCell
                        key={col.name}
                        className={cn('h-(--row-h) max-w-80 text-(length:--text-body)', col.dataType === 'number' && 'text-right tabular-nums')}
                      >
                        <CellValue value={row[col.name]} dataType={col.dataType} />
                      </TableCell>
                    ))}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </DndContext>
        </div>
      )}

      <ResultsPagination
        page={view.page}
        pageCount={view.pageCount}
        pageSize={view.pageSize}
        totalFiltered={view.totalFiltered}
        onPageChange={view.setPage}
        onPageSizeChange={view.setPageSize}
      />
    </div>
  )
}

function SortableHeader({
  column,
  sortDirection,
  onSort,
}: {
  column: QueryResultColumnDto
  sortDirection: 'asc' | 'desc' | null
  onSort: () => void
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: column.name })
  const style = { transform: CSS.Transform.toString(transform), transition }

  return (
    <TableHead
      ref={setNodeRef}
      style={style}
      className={cn('h-(--row-h) whitespace-nowrap', isDragging && 'relative z-20 bg-background opacity-90 shadow-md')}
    >
      <div className="flex items-center gap-1">
        <button
          type="button"
          className="cursor-grab touch-none text-muted-foreground/40 hover:text-foreground active:cursor-grabbing"
          aria-label={`Reorder ${column.name}`}
          {...attributes}
          {...listeners}
        >
          <GripVertical className="size-3.5" />
        </button>
        <button type="button" onClick={onSort} className="flex items-center gap-1.5 hover:text-primary">
          <DataTypeIcon dataType={column.dataType} />
          {column.name}
          {sortDirection === 'asc' && <ArrowUp className="size-3" />}
          {sortDirection === 'desc' && <ArrowDown className="size-3" />}
          {!sortDirection && <ChevronsUpDown className="size-3 opacity-30" />}
        </button>
      </div>
    </TableHead>
  )
}

function CellValue({ value, dataType }: { value: unknown; dataType: string }) {
  if (value === null || value === undefined) {
    return <span className="text-muted-foreground/50 italic">null</span>
  }
  if (dataType === 'boolean') {
    return value ? <Check className="size-4 text-success" /> : <XIcon className="size-4 text-muted-foreground" />
  }
  if (dataType === 'number' && typeof value === 'number') {
    return <>{value.toLocaleString()}</>
  }
  const text = String(value)
  return (
    <span className="block truncate" title={text}>
      {text}
    </span>
  )
}
