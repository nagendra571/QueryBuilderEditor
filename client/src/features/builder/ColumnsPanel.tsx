import {
  DndContext,
  type DragEndEvent,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
} from '@dnd-kit/core'
import { restrictToVerticalAxis, restrictToParentElement } from '@dnd-kit/modifiers'
import {
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { Eye, EyeOff, GripVertical, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { columnKey, useBuilderStore } from '@/features/builder/builder-store'
import { DataTypeIcon } from '@/lib/data-type-icons'
import { cn } from '@/lib/utils'
import type { QueryColumn } from '@/types'

export function ColumnsPanel() {
  const columns = useBuilderStore((s) => s.definition.columns)
  const reorderColumns = useBuilderStore((s) => s.reorderColumns)

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )

  function handleDragEnd(event: DragEndEvent) {
    const { active, over } = event
    if (!over || active.id === over.id) return
    const fromIndex = columns.findIndex((c) => columnKey(c.tableAlias, c.columnName, c.aggregate) === active.id)
    const toIndex = columns.findIndex((c) => columnKey(c.tableAlias, c.columnName, c.aggregate) === over.id)
    if (fromIndex !== -1 && toIndex !== -1) reorderColumns(fromIndex, toIndex)
  }

  if (columns.length === 0) {
    return (
      <div className="rounded-md border border-dashed border-border p-(--space-section) text-center text-xs text-muted-foreground">
        No columns selected yet. Add columns from the catalog on the left.
      </div>
    )
  }

  const ids = columns.map((c) => columnKey(c.tableAlias, c.columnName, c.aggregate))

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={closestCenter}
      onDragEnd={handleDragEnd}
      modifiers={[restrictToVerticalAxis, restrictToParentElement]}
    >
      <SortableContext items={ids} strategy={verticalListSortingStrategy}>
        <div className="flex flex-col gap-1">
          {columns.map((column) => (
            <SortableColumnRow key={columnKey(column.tableAlias, column.columnName, column.aggregate)} column={column} />
          ))}
        </div>
      </SortableContext>
    </DndContext>
  )
}

function SortableColumnRow({ column }: { column: QueryColumn }) {
  const key = columnKey(column.tableAlias, column.columnName, column.aggregate)
  const removeColumn = useBuilderStore((s) => s.removeColumn)
  const renameColumnAlias = useBuilderStore((s) => s.renameColumnAlias)
  const toggleColumnVisible = useBuilderStore((s) => s.toggleColumnVisible)

  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: key })

  const style = { transform: CSS.Transform.toString(transform), transition }

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={cn(
        'flex items-center gap-2 rounded-md border border-border bg-card px-2 py-(--space-row-y)',
        isDragging && 'opacity-60 shadow-lg',
        !column.isVisible && 'opacity-50',
      )}
    >
      <button
        type="button"
        className="cursor-grab touch-none text-muted-foreground hover:text-foreground active:cursor-grabbing"
        aria-label={`Reorder ${column.columnName}`}
        {...attributes}
        {...listeners}
      >
        <GripVertical className="size-3.5" />
      </button>

      <DataTypeIcon dataType={column.dataType} />

      <span className="w-36 shrink-0 truncate text-xs text-muted-foreground" title={column.columnName}>
        {column.columnName}
      </span>

      <Input
        value={column.alias ?? ''}
        onChange={(e) => renameColumnAlias(key, e.target.value)}
        placeholder={column.columnName}
        className="h-7 flex-1 text-xs"
        aria-label={`Alias for ${column.columnName}`}
      />

      <Button
        variant="ghost"
        size="icon"
        className="size-6"
        onClick={() => toggleColumnVisible(key)}
        aria-label={column.isVisible ? 'Hide column' : 'Show column'}
      >
        {column.isVisible ? <Eye className="size-3.5" /> : <EyeOff className="size-3.5" />}
      </Button>

      <Button
        variant="ghost"
        size="icon"
        className="size-6 text-destructive hover:text-destructive"
        onClick={() => removeColumn(key)}
        aria-label={`Remove ${column.columnName}`}
      >
        <X className="size-3.5" />
      </Button>
    </div>
  )
}
