import { memo } from 'react'
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
import { SortableContext, horizontalListSortingStrategy, sortableKeyboardCoordinates } from '@dnd-kit/sortable'
import type { QueryResultColumnDto } from '@/types'
import { GridHeaderCell, type HeaderActions } from './GridHeaderCell'
import { ROW_NUM_WIDTH, type ColumnLayout } from './grid-layout.ts'
import type { GridSort } from './grid-view.ts'

// Memoised so scrolling the body does not re-render every header and its sortable hooks.
export const GridHeaderRow = memo(function GridHeaderRow({
  columns,
  layouts,
  sort,
  height,
  width,
  actionsFor,
  onReorder,
  filterOpenFor,
  onFilterOpenChange,
  renderFilter,
}: {
  columns: QueryResultColumnDto[]
  layouts: ColumnLayout[]
  sort: GridSort | null
  height: number
  width: number
  actionsFor: (name: string) => HeaderActions
  onReorder: (active: string, over: string) => void
  filterOpenFor: string | null
  onFilterOpenChange: (column: string, open: boolean) => void
  renderFilter: (column: QueryResultColumnDto) => React.ReactNode
}) {
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )

  function onDragEnd({ active, over }: DragEndEvent) {
    if (over && active.id !== over.id) onReorder(String(active.id), String(over.id))
  }

  const ids = (group: ColumnLayout['group']) => layouts.filter((l) => l.group === group).map((l) => l.name)

  return (
    <div
      role="row"
      aria-rowindex={1}
      className="sticky top-0 z-20 flex border-b border-foreground bg-muted"
      style={{ width, minWidth: '100%', height }}
    >
      <div
        role="columnheader"
        aria-colindex={1}
        className="sticky left-0 z-[15] flex shrink-0 items-center justify-end border-r border-border/60 bg-muted px-2.5 font-numeric text-xs text-muted-foreground"
        style={{ width: ROW_NUM_WIDTH }}
      >
        #
      </div>
      <DndContext
        sensors={sensors}
        collisionDetection={closestCenter}
        onDragEnd={onDragEnd}
        modifiers={[restrictToHorizontalAxis, restrictToParentElement]}
        // The default live region lands inside role="row", where only cells are allowed.
        accessibility={{ container: document.body }}
      >
        {(['left', 'centre', 'right'] as const).map((group) => [
          group === 'right' && <div key="filler" aria-hidden className="min-w-0 flex-1" />,
          <SortableContext key={group} items={ids(group)} strategy={horizontalListSortingStrategy}>
            {columns.map((col, i) =>
              layouts[i].group === group ? (
                <GridHeaderCell
                  key={col.name}
                  column={col}
                  layout={layouts[i]}
                  colIndex={i}
                  sortDirection={sort?.column === col.name ? sort.direction : null}
                  actions={actionsFor(col.name)}
                  filterOpen={filterOpenFor === col.name}
                  onFilterOpenChange={(open) => onFilterOpenChange(col.name, open)}
                  filterContent={filterOpenFor === col.name ? renderFilter(col) : null}
                />
              ) : null,
            )}
          </SortableContext>,
        ])}
      </DndContext>
    </div>
  )
})
