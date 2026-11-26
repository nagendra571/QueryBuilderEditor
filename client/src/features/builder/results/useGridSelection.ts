import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { Row } from '@tanstack/react-table'
import { toast } from 'sonner'
import type { QueryResultColumnDto } from '@/types'
import { buildCopyText } from './copy-selection.ts'
import { copyText } from './copy-text.ts'
import {
  clampRange,
  moveFocus,
  normalize,
  rangeSize,
  selectAll,
  type CellPos,
  type CellRange,
  type MoveKey,
} from './selection.ts'
import { restoredSelection, sameView, type GridMemory, type SelectionEntry } from './grid-memory.ts'
import type { RowData } from './grid-table.ts'

const count = (n: number, noun: string) => `${n.toLocaleString()} ${noun}${n === 1 ? '' : 's'}`

const KEYS: Record<string, MoveKey> = {
  ArrowUp: 'up',
  ArrowDown: 'down',
  ArrowLeft: 'left',
  ArrowRight: 'right',
  Home: 'home',
  End: 'end',
  PageUp: 'pageUp',
  PageDown: 'pageDown',
}

export function useGridSelection({
  rows,
  columns,
  data,
  resetKey,
  pageRows,
  reveal,
  onEnter,
  onContextMenuKey,
  memory,
}: {
  rows: Row<RowData>[]
  columns: QueryResultColumnDto[]
  /** Identity of the run's rows: a new run drops the selection. */
  data: unknown
  /** Joined search, sort, order, visibility and pinning: any change drops the selection. */
  resetKey: string
  pageRows: () => number
  reveal: (pos: CellPos) => void
  onEnter?: (row: RowData) => void
  /** Shift+F10 or the ContextMenu key on the grid. */
  onContextMenuKey?: () => void
  /** Outlives the grid's remount on entering or leaving full screen. */
  memory?: GridMemory
}) {
  const [stored, setSel] = useState<SelectionEntry>(() => restoredSelection(memory, resetKey, data))
  let sel = stored
  // Stored as cleared, not just hidden, so the old range cannot come back when the key returns to its old value.
  if (!sameView(stored, resetKey, data)) {
    sel = { resetKey, data, range: null }
    setSel(sel)
  }
  const bounds = useMemo(() => ({ rows: rows.length, cols: columns.length }), [rows.length, columns.length])
  const range = useMemo(() => (sel.range ? clampRange(sel.range, bounds) : null), [sel, bounds])

  useEffect(() => {
    memory?.saveSelection(stored)
  }, [memory, stored])

  const update = useCallback(
    (fn: (current: CellRange | null) => CellRange | null) =>
      setSel((s) => ({ resetKey, data, range: fn(sameView(s, resetKey, data) ? s.range : null) })),
    [resetKey, data],
  )

  const dragging = useRef(false)
  useEffect(() => {
    const end = () => {
      dragging.current = false
    }
    document.addEventListener('pointerup', end)
    document.addEventListener('pointercancel', end)
    return () => {
      document.removeEventListener('pointerup', end)
      document.removeEventListener('pointercancel', end)
    }
  }, [])

  const copy = useCallback(() => {
    const text = buildCopyText(columns, rows.map((r) => r.original), range ? normalize(range) : null)
    if (!copyText(text)) {
      toast.error('Copy failed. Select cells and press Ctrl+C.')
      return
    }
    toast.success(range ? `Copied ${count(rangeSize(range), 'cell')}` : `Copied ${count(rows.length, 'row')}`)
  }, [columns, rows, range])

  const copyCell = useCallback(
    (pos: CellPos) => {
      const text = buildCopyText(
        columns,
        rows.map((r) => r.original),
        { top: pos.row, bottom: pos.row, left: pos.col, right: pos.col },
      )
      if (copyText(text)) toast.success('Copied 1 cell')
      else toast.error('Copy failed. Select cells and press Ctrl+C.')
    },
    [columns, rows],
  )

  const copyColumn = useCallback(
    (name: string) => {
      const column = columns.find((c) => c.name === name)
      if (!column) return
      if (!copyText(buildCopyText([column], rows.map((r) => r.original), null))) {
        toast.error('Copy failed. Select cells and press Ctrl+C.')
        return
      }
      toast.success(`Copied ${count(rows.length, 'value')}`)
    },
    [columns, rows],
  )

  const moveTo = useCallback(
    (pos: CellPos) => {
      if (bounds.rows === 0 || bounds.cols === 0) return
      const next = {
        row: Math.max(0, Math.min(pos.row, bounds.rows - 1)),
        col: Math.max(0, Math.min(pos.col, bounds.cols - 1)),
      }
      update(() => ({ anchor: next, focus: next }))
      reveal(next)
    },
    [bounds, update, reveal],
  )

  const onCellPointerDown = useCallback(
    (row: number, col: number, e: React.PointerEvent) => {
      if (e.button !== 0) return
      const grid = e.currentTarget.closest('[role="grid"]') as HTMLElement | null
      grid?.focus({ preventScroll: true })
      const pos = { row, col }
      const extend = e.shiftKey
      update((cur) => ({ anchor: extend && cur ? cur.anchor : pos, focus: pos }))
      // Touch pointers are captured by the browser for scrolling, so a drag could never extend anyway.
      dragging.current = e.pointerType !== 'touch'
    },
    [update],
  )

  const onCellPointerEnter = useCallback(
    (row: number, col: number) => {
      if (!dragging.current) return
      update((cur) => (cur ? { anchor: cur.anchor, focus: { row, col } } : cur))
    },
    [update],
  )

  const onKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.target !== e.currentTarget) return
      const mod = e.ctrlKey || e.metaKey
      const key = e.key.toLowerCase()

      if (e.key === 'ContextMenu' || (e.shiftKey && e.key === 'F10')) {
        if (onContextMenuKey) {
          e.preventDefault()
          onContextMenuKey()
        }
        return
      }
      if (mod && key === 'a') {
        e.preventDefault()
        update(() => selectAll(bounds))
        return
      }
      if (mod && key === 'c') {
        if (range) {
          e.preventDefault()
          copy()
        }
        return
      }
      if (e.key === 'Escape') {
        if (range) {
          // Consumed here, so the same press does not also leave full screen.
          e.preventDefault()
          update(() => null)
        }
        return
      }
      if (e.key === 'Enter') {
        if (range && onEnter && rows[range.focus.row]) {
          e.preventDefault()
          onEnter(rows[range.focus.row].original)
        }
        return
      }

      let move = KEYS[e.key]
      if (mod && e.key === 'Home') move = 'ctrlHome'
      if (mod && e.key === 'End') move = 'ctrlEnd'
      if (!move) return
      e.preventDefault()
      if (bounds.rows === 0 || bounds.cols === 0) return
      const next = range ? moveFocus(range.focus, move, bounds, pageRows()) : { row: 0, col: 0 }
      const anchor = e.shiftKey && range ? range.anchor : next
      update(() => ({ anchor, focus: next }))
      reveal(next)
    },
    [bounds, range, rows, copy, update, onEnter, onContextMenuKey, pageRows, reveal],
  )

  return { range, copy, copyCell, copyColumn, moveTo, onKeyDown, onCellPointerDown, onCellPointerEnter }
}
