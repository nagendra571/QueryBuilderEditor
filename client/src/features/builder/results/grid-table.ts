import type { ColumnDef, TableOptions } from '@tanstack/table-core'
import type { QueryResultColumnDto } from '@/types'
import { compareCells } from './compare-cells.ts'
import { MAX_COL_WIDTH, MIN_COL_WIDTH } from './column-widths.ts'
import { matchesSearch } from './grid-columns.ts'
import { matchesFilter, type GridFilter } from './grid-filters.ts'

export type RowData = Record<string, unknown>

// TanStack negates the comparator for descending, which reproduces the old order:
// nulls last ascending, first descending. So no sortUndefined/invertSorting here.
export function buildColumnDefs(columns: QueryResultColumnDto[]): ColumnDef<RowData>[] {
  return columns.map((c) => ({
    id: c.name,
    accessorFn: (row) => row[c.name],
    minSize: MIN_COL_WIDTH,
    maxSize: MAX_COL_WIDTH,
    enableMultiSort: false,
    sortDescFirst: false,
    sortUndefined: false,
    filterFn: (row, id, f: GridFilter) => matchesFilter(row.getValue(id), f, c.dataType),
    sortingFn: (a, b, id) => compareCells(a.getValue(id), b.getValue(id), c.dataType === 'number'),
  }))
}

export const tableBehaviour: Pick<
  TableOptions<RowData>,
  'enableMultiSort' | 'sortDescFirst' | 'getColumnCanGlobalFilter' | 'globalFilterFn'
> = {
  enableMultiSort: false,
  sortDescFirst: false,
  getColumnCanGlobalFilter: (column) => column.getIsVisible(),
  globalFilterFn: (row, columnId, filterValue) => matchesSearch(row.getValue(columnId), String(filterValue ?? '')),
}
