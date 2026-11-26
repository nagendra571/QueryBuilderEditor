import { X } from 'lucide-react'
import { useRef } from 'react'
import { Button } from '@/components/ui/button'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { cn } from '@/lib/utils'
import {
  PANEL_MIN,
  PANEL_STEP,
  clampPanelWidth,
  type PanelState,
  type PanelTab,
} from './panel-layout.ts'

// The tab body is a column so each view can pin its header and scroll only its own list.
const TAB_BODY =
  'flex min-h-0 flex-1 flex-col focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring'

export function DetailsPanel({
  state,
  onChange,
  onClose,
  mode,
  areaWidth,
  row,
  column,
  format,
}: {
  state: PanelState
  onChange: (s: PanelState) => void
  onClose: () => void
  mode: 'dock' | 'overlay'
  areaWidth: number
  row: React.ReactNode
  column: React.ReactNode
  format: React.ReactNode
}) {
  const drag = useRef<{ startX: number; startWidth: number } | null>(null)
  const width = clampPanelWidth(state.width, areaWidth)
  const setWidth = (w: number) => onChange({ ...state, width: clampPanelWidth(w, areaWidth) })

  return (
    <section
      aria-label="Details"
      style={{ width }}
      className={cn(
        'relative flex min-h-0 shrink-0 flex-col border-l border-border bg-background',
        mode === 'overlay' && 'absolute inset-y-0 right-0 z-10',
      )}
      onKeyDown={(e) => {
        if (e.key !== 'Escape' || e.defaultPrevented) return
        if ((e.target as Element).closest('[role="menu"],[role="listbox"],[role="dialog"]')) return
        // The full-screen listener on the document skips a defaultPrevented press, so this one only closes the panel.
        e.preventDefault()
        onClose()
      }}
    >
      <div
        role="separator"
        aria-orientation="vertical"
        aria-valuenow={width}
        aria-valuemin={PANEL_MIN}
        aria-valuemax={clampPanelWidth(Infinity, areaWidth)}
        aria-label="Resize details panel"
        tabIndex={0}
        className="absolute inset-y-0 -left-px z-10 w-1.5 cursor-col-resize touch-none hover:bg-primary/30 focus-visible:bg-primary/40 focus-visible:outline-2 focus-visible:outline-ring"
        onPointerDown={(e) => {
          if (e.button !== 0) return
          e.preventDefault()
          e.currentTarget.setPointerCapture(e.pointerId)
          drag.current = { startX: e.clientX, startWidth: width }
        }}
        onPointerMove={(e) => {
          if (drag.current) setWidth(drag.current.startWidth + (drag.current.startX - e.clientX))
        }}
        onPointerUp={() => (drag.current = null)}
        onPointerCancel={() => (drag.current = null)}
        onKeyDown={(e) => {
          if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return
          e.preventDefault()
          setWidth(width + (e.key === 'ArrowLeft' ? PANEL_STEP : -PANEL_STEP))
        }}
      />
      <Tabs
        value={state.tab}
        onValueChange={(tab) => onChange({ ...state, tab: tab as PanelTab })}
        className="min-h-0 flex-1 gap-0"
      >
        <div className="flex items-center justify-between gap-2 border-b border-border px-2 py-1">
          <TabsList variant="line" className="h-8">
            <TabsTrigger value="row" className="px-2">
              Row
            </TabsTrigger>
            <TabsTrigger value="column" className="px-2">
              Column
            </TabsTrigger>
            <TabsTrigger value="format" className="px-2">
              Format
            </TabsTrigger>
          </TabsList>
          <Button variant="ghost" size="icon" className="size-7" aria-label="Close details" onClick={onClose}>
            <X className="size-3.5" />
          </Button>
        </div>
        <TabsContent value="row" className={TAB_BODY}>
          {row}
        </TabsContent>
        <TabsContent value="column" className={TAB_BODY}>
          {column}
        </TabsContent>
        <TabsContent value="format" className={TAB_BODY}>
          {format}
        </TabsContent>
      </Tabs>
    </section>
  )
}
