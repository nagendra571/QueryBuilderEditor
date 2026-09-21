import { useEffect, useMemo, useState } from 'react'
import type { QueryResultDto } from '@/types'

export type SortDirection = 'asc' | 'desc'
export type PageSize = 25 | 50 | 100 | 250 | 'all'

export const PAGE_SIZE_OPTIONS: PageSize[] = [25, 50, 100, 250, 'all']

interface SortState {
  column: string
  direction: SortDirection
}

function compareCells(a: unknown, b: unknown): number {
  const aNull = a === null || a === undefined
  const bNull = b === null || b === undefined
  if (aNull && bNull) return 0
  if (aNull) return 1 // nulls always sort last
  if (bNull) return -1

  if (typeof a === 'number' && typeof b === 'number') return a - b
  if (typeof a === 'boolean' && typeof b === 'boolean') return a === b ? 0 : a ? 1 : -1
  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' })
}

export function useResultsView(result: QueryResultDto) {
  const [columnOrder, setColumnOrder] = useState<string[]>(() => result.columns.map((c) => c.name))
  const [hiddenColumns, setHiddenColumns] = useState<Set<string>>(new Set())
  const [sort, setSort] = useState<SortState | null>(null)
  const [search, setSearch] = useState('')
  const [pageSize, setPageSize] = useState<PageSize>(50)
  const [page, setPage] = useState(1)

  // A fresh run produces a new `result` reference — reset the view to defaults for it.
  useEffect(() => {
    setColumnOrder(result.columns.map((c) => c.name))
    setHiddenColumns(new Set())
    setSort(null)
    setSearch('')
    setPageSize(50)
    setPage(1)
  }, [result])

  useEffect(() => {
    setPage(1)
  }, [search, pageSize, sort])

  const columnsByName = useMemo(() => new Map(result.columns.map((c) => [c.name, c])), [result.columns])
  const visibleColumns = useMemo(
    () => columnOrder.map((name) => columnsByName.get(name)).filter((c): c is (typeof result.columns)[number] => !!c && !hiddenColumns.has(c.name)),
    [columnOrder, columnsByName, hiddenColumns],
  )

  const filteredRows = useMemo(() => {
    if (!search.trim()) return result.rows
    const term = search.trim().toLowerCase()
    return result.rows.filter((row) => visibleColumns.some((c) => String(row[c.name] ?? '').toLowerCase().includes(term)))
  }, [result.rows, search, visibleColumns])

  const sortedRows = useMemo(() => {
    if (!sort) return filteredRows
    const direction = sort.direction === 'asc' ? 1 : -1
    return [...filteredRows].sort((a, b) => direction * compareCells(a[sort.column], b[sort.column]))
  }, [filteredRows, sort])

  const pageCount = pageSize === 'all' ? 1 : Math.max(1, Math.ceil(sortedRows.length / pageSize))
  const currentPage = Math.min(page, pageCount)

  const pagedRows = useMemo(() => {
    if (pageSize === 'all') return sortedRows
    const start = (currentPage - 1) * pageSize
    return sortedRows.slice(start, start + pageSize)
  }, [sortedRows, pageSize, currentPage])

  const rowNumberOffset = pageSize === 'all' ? 0 : (currentPage - 1) * pageSize

  function toggleSort(columnName: string) {
    setSort((prev) => {
      if (!prev || prev.column !== columnName) return { column: columnName, direction: 'asc' }
      if (prev.direction === 'asc') return { column: columnName, direction: 'desc' }
      return null
    })
  }

  function toggleColumnVisible(columnName: string) {
    setHiddenColumns((prev) => {
      const next = new Set(prev)
      if (next.has(columnName)) next.delete(columnName)
      else next.add(columnName)
      return next
    })
  }

  function reorderColumn(fromName: string, toName: string) {
    setColumnOrder((prev) => {
      const fromIndex = prev.indexOf(fromName)
      const toIndex = prev.indexOf(toName)
      if (fromIndex === -1 || toIndex === -1) return prev
      const next = [...prev]
      const [moved] = next.splice(fromIndex, 1)
      next.splice(toIndex, 0, moved)
      return next
    })
  }

  function resetView() {
    setColumnOrder(result.columns.map((c) => c.name))
    setHiddenColumns(new Set())
    setSort(null)
    setSearch('')
    setPageSize(50)
    setPage(1)
  }

  const isDefaultView =
    hiddenColumns.size === 0 &&
    !sort &&
    !search.trim() &&
    pageSize === 50 &&
    columnOrder.every((name, i) => name === result.columns[i]?.name)

  return {
    allColumns: result.columns,
    visibleColumns,
    hiddenColumns,
    sort,
    search,
    setSearch,
    pageSize,
    setPageSize,
    page: currentPage,
    setPage,
    pageCount,
    pagedRows,
    totalFiltered: sortedRows.length,
    totalRaw: result.rows.length,
    rowNumberOffset,
    toggleSort,
    toggleColumnVisible,
    reorderColumn,
    resetView,
    isDefaultView,
  }
}
