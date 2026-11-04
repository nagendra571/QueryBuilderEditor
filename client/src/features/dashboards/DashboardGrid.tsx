import ReactGridLayout, { useContainerWidth, type LayoutItem } from 'react-grid-layout'
import 'react-grid-layout/css/styles.css'
import 'react-resizable/css/styles.css'
import {
  ArrowDown,
  ArrowLeft,
  ArrowRight,
  ArrowUp,
  Ban,
  ChevronsUpDown,
  GripVertical,
  Lock,
  MoreVertical,
  Pencil,
  SlidersHorizontal,
  Trash2,
} from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { ChartTileBody } from '@/features/dashboards/ChartTile'
import {
  applyBox,
  GAP,
  GRID_COLS,
  MAX_H,
  MIN_H,
  MIN_W,
  NARROW_BELOW,
  neighbourRow,
  pixelsForRows,
  readingOrder,
  ROW_HEIGHT,
  type Box,
} from '@/features/dashboards/layout'
import { SectionTile } from '@/features/dashboards/SectionTile'
import { SECTION_H } from '@/features/dashboards/section'
import { useAutoHeight } from '@/features/dashboards/useAutoHeight'
import { cn } from '@/lib/utils'
import type { DashboardTileDto } from '@/types'

/** A tile as edited on the page: the server's shape plus a key that is stable before it has an id. */
export type DraftTile = DashboardTileDto & { key: string; isNew?: boolean }

export const WIDTHS: Array<{ value: number; label: string }> = [
  { value: 3, label: 'Quarter (3 of 12)' },
  { value: 4, label: 'Third (4 of 12)' },
  { value: 6, label: 'Half (6 of 12)' },
  { value: 8, label: 'Two thirds (8 of 12)' },
  { value: 12, label: 'Full width (12 of 12)' },
]
export const HEIGHTS: Array<{ value: number; label: string }> = [
  { value: 3, label: 'Small (3 rows)' },
  { value: 6, label: 'Medium (6 rows)' },
  { value: 9, label: 'Large (9 rows)' },
]

function tileLabel(tile: DraftTile, index: number): string {
  if (tile.kind === 'section') return tile.title?.trim() || `Section, tile ${index + 1}`
  if (tile.restricted) return `Restricted chart, tile ${index + 1}`
  if (tile.kind === 'text') return tile.title?.trim() || `Text note: ${(tile.text ?? '').slice(0, 30)}`
  if (tile.missing) return `Removed chart, tile ${index + 1}`
  return tile.title?.trim() || tile.chart?.visualizationName || `Chart, tile ${index + 1}`
}

/** Takes the grid's own layout back into the draft; returns the same array when nothing moved, so it can't loop. */
function mergeLayout(current: DraftTile[], items: readonly LayoutItem[]): DraftTile[] {
  const byKey = new Map(items.map((item) => [item.i, item]))
  let changed = false
  const next = current.map((t) => {
    const item = byKey.get(t.key)
    if (!item || (item.x === t.x && item.y === t.y && item.w === t.width && item.h === t.height)) return t
    changed = true
    return { ...t, x: item.x, y: item.y, width: item.w, height: item.h }
  })
  return changed ? next : current
}

export function DashboardGrid({
  tiles,
  editing,
  onChange,
  onRemove,
  onEditText,
  onEditParameters,
  onNarrowChange,
}: {
  tiles: DraftTile[]
  editing: boolean
  onChange: (fn: (tiles: DraftTile[]) => DraftTile[]) => void
  onRemove: (key: string) => void
  onEditText: (key: string) => void
  onEditParameters?: (key: string) => void
  onNarrowChange?: (narrow: boolean) => void
}) {
  const { width, containerRef, mounted } = useContainerWidth()
  const narrow = mounted && width < NARROW_BELOW
  const [settled, setSettled] = useState(false)
  const [measured, setMeasured] = useState<Record<string, number>>({})
  const [announcement, setAnnouncement] = useState('')
  const announceFlip = useRef(false)

  useEffect(() => {
    onNarrowChange?.(narrow)
  }, [narrow, onNarrowChange])

  // The first paint places tiles without animating; only later moves are eased.
  useEffect(() => {
    if (!mounted) return
    const frame = requestAnimationFrame(() => setSettled(true))
    return () => cancelAnimationFrame(frame)
  }, [mounted])

  const announce = useCallback((message: string) => {
    announceFlip.current = !announceFlip.current
    // Alternating a trailing character makes a repeated message still count as a change.
    setAnnouncement(message + (announceFlip.current ? '' : '​'))
  }, [])

  const ordered = readingOrder(tiles)
  const labelOf = (tile: DraftTile) => tileLabel(tile, tiles.indexOf(tile))

  function applyKeyboard(tile: DraftTile, patch: Partial<Box> & { autoHeight?: boolean }, describe: (t: DraftTile) => string) {
    const { autoHeight, ...box } = patch
    const stepped = applyBox(tiles, tile.key, box)
    const result = stepped.find((t) => t.key === tile.key)
    onChange((current) => {
      const next = applyBox(current, tile.key, box)
      return autoHeight === undefined ? next : next.map((t) => (t.key === tile.key ? { ...t, autoHeight } : t))
    })
    if (result) announce(describe(result))
  }

  function onAutoRows(tile: DraftTile, rows: number) {
    // A narrow screen stacks tiles at a different width; its measurements must never reach the saved layout.
    if (narrow) return
    if (editing) {
      onChange((current) => current.map((t) => (t.key === tile.key && t.autoHeight && t.height !== rows ? { ...t, height: rows } : t)))
    } else {
      setMeasured((m) => (m[tile.key] === rows ? m : { ...m, [tile.key]: rows }))
    }
  }

  const heightOf = (t: DraftTile) => (!editing && t.autoHeight && measured[t.key] ? measured[t.key] : t.height)

  const sectionProps = (tile: DraftTile) => ({
    tile,
    label: labelOf(tile),
    editing: editing && !narrow,
    onRemove: () => onRemove(tile.key),
    onEditText: () => onEditText(tile.key),
    onEditParameters: onEditParameters ? () => onEditParameters(tile.key) : undefined,
    onAutoRows: (rows: number) => onAutoRows(tile, rows),
    onLayoutAction: (patch: Partial<Box> & { autoHeight?: boolean }, describe: (t: DraftTile) => string) =>
      applyKeyboard(tile, patch, describe),
    neighbour: (direction: 'up' | 'down') => neighbourRow(tiles, tile.key, direction),
    announce,
  })

  const layout: LayoutItem[] = ordered.map((t) =>
    t.kind === 'section'
      ? { i: t.key, x: 0, y: t.y, w: GRID_COLS, h: SECTION_H, minW: GRID_COLS, maxW: GRID_COLS, minH: SECTION_H, maxH: SECTION_H, isResizable: false }
      : { i: t.key, x: t.x, y: t.y, w: t.width, h: heightOf(t), minW: MIN_W, maxW: GRID_COLS, minH: MIN_H, maxH: MAX_H },
  )

  return (
    <div ref={containerRef} className="flex flex-col gap-3">
      {narrow && editing && (
        <p className="rounded-sm border border-dashed border-border bg-muted px-3 py-2 text-xs text-muted-foreground">
          Arranging tiles needs a wider screen. Tiles are shown in reading order here; nothing about their layout is changed.
        </p>
      )}

      {mounted && narrow && (
        <div className="flex flex-col gap-4">
          {ordered.map((tile) => (
            <TileSection key={tile.key} {...sectionProps(tile)} heightPx={tile.autoHeight ? undefined : pixelsForRows(tile.height)} />
          ))}
        </div>
      )}

      {mounted && !narrow && (
        <div className="dash-grid-wrap" data-settled={settled}>
        <ReactGridLayout
          width={width}
          layout={layout}
          className={cn('dash-grid', editing && 'is-editing survey-grid')}
          gridConfig={{ cols: GRID_COLS, rowHeight: ROW_HEIGHT, margin: [GAP, GAP], containerPadding: [0, 0] }}
          dragConfig={{
            enabled: editing,
            // The library only exempts its own handle class; ours must be listed or a corner drag moves the tile.
            cancel: '.dash-resize-handle, button, a, input, textarea, select, [role="menu"], [data-no-drag]',
          }}
          resizeConfig={{
            enabled: editing,
            handles: ['se'],
            handleComponent: (_axis, ref) => <span ref={ref as React.Ref<HTMLSpanElement>} className="dash-resize-handle" aria-hidden="true" />,
          }}
          onLayoutChange={(next) => {
            if (editing) onChange((current) => mergeLayout(current, next))
          }}
          onResizeStop={(next, oldItem, newItem) => {
            if (!editing || !oldItem || !newItem) return
            const heightChanged = oldItem.h !== newItem.h
            onChange((current) => {
              const merged = mergeLayout(current, next)
              return heightChanged ? merged.map((t) => (t.key === newItem.i ? { ...t, autoHeight: false } : t)) : merged
            })
          }}
        >
          {ordered.map((tile) => (
            <div key={tile.key} className={cn('dash-item', tile.kind === 'section' && 'dash-item-fixed')}>
              <TileSection {...sectionProps(tile)} fillParent />
            </div>
          ))}
        </ReactGridLayout>
        </div>
      )}

      <p role="status" aria-live="polite" className="sr-only">
        {announcement}
      </p>
    </div>
  )
}

type LayoutAction = (patch: Partial<Box> & { autoHeight?: boolean }, describe: (t: DraftTile) => string) => void

function TileSection({
  tile,
  label,
  editing,
  fillParent,
  heightPx,
  onRemove,
  onEditText,
  onEditParameters,
  onAutoRows,
  onLayoutAction,
  neighbour,
  announce,
}: {
  tile: DraftTile
  label: string
  editing: boolean
  fillParent?: boolean
  heightPx?: number
  onRemove: () => void
  onEditText: () => void
  onEditParameters?: () => void
  onAutoRows: (rows: number) => void
  onLayoutAction: LayoutAction
  neighbour: (direction: 'up' | 'down') => number | null
  announce: (message: string) => void
}) {
  const titleId = `tile-title-${tile.key}`
  const statusId = `tile-status-${tile.key}`
  const isLive = tile.kind === 'chart' && !tile.restricted && !tile.missing && !!tile.chart
  const canFit = tile.kind === 'text' || isLive
  const contentRef = useAutoHeight(tile.autoHeight && canFit, onAutoRows)
  const natural = tile.autoHeight && canFit

  const moved = (t: DraftTile) => `${label} moved to column ${t.x + 1}, row ${t.y + 1}`
  const resized = (t: DraftTile) => `${label} resized to ${t.width} columns, ${t.height} rows`

  if (tile.kind === 'section') {
    return (
      <SectionTile
        titleId={titleId}
        title={tile.title?.trim() ?? ''}
        description={tile.text}
        heightPx={heightPx}
        fillParent={fillParent}
        editing={editing}
        label={label}
        onEdit={onEditText}
        onRemove={onRemove}
        onMove={(direction) => {
          const row = neighbour(direction)
          if (row === null) announce(`${label} is already at the ${direction === 'up' ? 'top' : 'bottom'}`)
          else onLayoutAction({ y: row }, moved)
        }}
      />
    )
  }

  return (
    <section
      aria-labelledby={titleId}
      style={heightPx ? { height: heightPx } : undefined}
      className={cn(
        'min-w-0 overflow-hidden rounded-md border border-border bg-card p-3',
        fillParent && 'h-full',
        tile.restricted && 'zone-hatch',
        isLive && !editing && 'border-dashed border-[var(--parcel)]',
        editing && 'border-foreground/35',
      )}
    >
      <div className={cn('flex min-h-0 min-w-0 flex-col gap-2', !natural && 'h-full')} ref={contentRef}>
        {editing && (
          <div className="-mx-3 -mt-3 flex h-9 shrink-0 items-center gap-1 border-b border-dashed border-foreground/25 bg-muted px-2">
            <span className="text-muted-foreground" aria-hidden="true">
              <GripVertical className="size-4" />
            </span>
            <span className="min-w-0 flex-1" />
            <span className="shrink-0 font-numeric text-xs text-muted-foreground" aria-hidden>
              {tile.width}/12
            </span>
            {tile.kind === 'text' && (
              <Button variant="ghost" size="icon" className="size-6" aria-label={`Edit ${label}`} onClick={onEditText}>
                <Pencil className="size-3.5" />
              </Button>
            )}
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon" className="size-6" aria-label={`Layout of ${label}`}>
                  <MoreVertical className="size-3.5" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="max-h-96 w-56 overflow-y-auto">
                <DropdownMenuGroup>
                  <DropdownMenuLabel>Move</DropdownMenuLabel>
                  <Step
                    icon={<ArrowLeft className="size-3.5" />}
                    onSelect={() =>
                      tile.x > 0 ? onLayoutAction({ x: tile.x - 1 }, moved) : announce(`${label} is already at the left edge`)
                    }
                  >
                    Move left
                  </Step>
                  <Step
                    icon={<ArrowRight className="size-3.5" />}
                    onSelect={() =>
                      tile.x + tile.width < GRID_COLS
                        ? onLayoutAction({ x: tile.x + 1 }, moved)
                        : announce(`${label} is already at the right edge`)
                    }
                  >
                    Move right
                  </Step>
                  <Step
                    icon={<ArrowUp className="size-3.5" />}
                    onSelect={() => {
                      const row = neighbour('up')
                      if (row === null) announce(`${label} is already at the top of its columns`)
                      else onLayoutAction({ y: row }, moved)
                    }}
                  >
                    Move up
                  </Step>
                  <Step
                    icon={<ArrowDown className="size-3.5" />}
                    onSelect={() => {
                      const row = neighbour('down')
                      if (row === null) announce(`${label} is already at the bottom of its columns`)
                      else onLayoutAction({ y: row }, moved)
                    }}
                  >
                    Move down
                  </Step>
                </DropdownMenuGroup>
                <DropdownMenuSeparator />
                <DropdownMenuGroup>
                  <DropdownMenuLabel>Width</DropdownMenuLabel>
                  <Step onSelect={() => onLayoutAction({ width: tile.width + 1 }, resized)}>Wider</Step>
                  <Step onSelect={() => onLayoutAction({ width: tile.width - 1 }, resized)}>Narrower</Step>
                  {WIDTHS.map((w) => (
                    <DropdownMenuItem key={w.value} onSelect={() => onLayoutAction({ width: w.value }, resized)}>
                      {w.label}
                    </DropdownMenuItem>
                  ))}
                </DropdownMenuGroup>
                <DropdownMenuSeparator />
                <DropdownMenuGroup>
                  <DropdownMenuLabel>Height</DropdownMenuLabel>
                  <Step onSelect={() => onLayoutAction({ height: tile.height + 1, autoHeight: false }, resized)}>Taller</Step>
                  <Step onSelect={() => onLayoutAction({ height: tile.height - 1, autoHeight: false }, resized)}>Shorter</Step>
                  {HEIGHTS.map((h) => (
                    <DropdownMenuItem key={h.value} onSelect={() => onLayoutAction({ height: h.value, autoHeight: false }, resized)}>
                      {h.label}
                    </DropdownMenuItem>
                  ))}
                  {canFit && (
                    <DropdownMenuItem
                      onSelect={() => {
                        onLayoutAction({ autoHeight: !tile.autoHeight }, () =>
                          tile.autoHeight ? `${label} height is now fixed` : `${label} height now fits its content`,
                        )
                      }}
                    >
                      <ChevronsUpDown className="size-3.5" />
                      {tile.autoHeight ? 'Fixed height' : 'Fit height to content'}
                    </DropdownMenuItem>
                  )}
                </DropdownMenuGroup>
                <DropdownMenuSeparator />
                {isLive && onEditParameters && (
                  <DropdownMenuItem onSelect={onEditParameters}>
                    <SlidersHorizontal className="size-3.5" />
                    Parameters…
                  </DropdownMenuItem>
                )}
                <DropdownMenuItem variant="destructive" onSelect={onRemove}>
                  <Trash2 className="size-3.5" />
                  Remove tile
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        )}

        {isLive && <ChartTileBody tile={tile} titleId={titleId} statusId={statusId} />}

        {tile.kind === 'text' && (
          <>
            <h2 id={titleId} className={cn('shrink-0 truncate text-sm font-semibold', !tile.title?.trim() && 'sr-only')}>
              {tile.title?.trim() || 'Note'}
            </h2>
            {/* Plain text only: never HTML or markdown, so nothing typed here can run as script. */}
            <p className={cn('text-sm break-words whitespace-pre-wrap', !natural && 'min-h-0 flex-1 overflow-auto')}>{tile.text}</p>
          </>
        )}

        {tile.restricted && (
          <>
            <h2 id={titleId} className="shrink-0 text-sm font-semibold">
              Restricted chart
            </h2>
            <div className="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 text-center text-xs text-muted-foreground">
              <span className="rounded-full border border-border bg-card p-2" aria-hidden="true">
                <Lock className="size-4" />
              </span>
              <span className="max-w-64 rounded-sm bg-card/90 px-2 py-1">
                You don&apos;t have access to the query behind this chart. Ask its owner to share it with you.
              </span>
            </div>
          </>
        )}

        {tile.missing && !tile.restricted && (
          <>
            <h2 id={titleId} className="shrink-0 truncate text-sm font-semibold">
              {tile.title?.trim() || 'Removed chart'}
            </h2>
            <div className="flex min-h-0 flex-1 flex-col items-center justify-center gap-1.5 text-center text-xs text-muted-foreground">
              <Ban className="size-4" aria-hidden="true" />
              <span>This chart was removed.{editing ? ' Remove the tile to clear it.' : ''}</span>
            </div>
          </>
        )}
      </div>
    </section>
  )
}

/** A menu item for a repeatable nudge: the menu stays open so a keyboard user can press it again. */
function Step({ children, icon, onSelect }: { children: React.ReactNode; icon?: React.ReactNode; onSelect: () => void }) {
  return (
    <DropdownMenuItem
      onSelect={(event) => {
        event.preventDefault()
        onSelect()
      }}
    >
      {icon}
      {children}
    </DropdownMenuItem>
  )
}
