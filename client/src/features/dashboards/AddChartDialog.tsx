import { Loader2 } from 'lucide-react'
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { typeLabel } from '@/features/visualizations/chart-data'
import { usePickableVisualizations } from '@/hooks/useDashboards'
import type { PickableVisualizationDto } from '@/types'

export function AddChartDialog({
  open,
  onOpenChange,
  onPick,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  onPick: (visualization: PickableVisualizationDto) => void
}) {
  const { data, isLoading, isError } = usePickableVisualizations(open)

  // Grouped by query name, in the order the server returned them (already sorted by query).
  const groups: Array<{ queryId: string; name: string; items: PickableVisualizationDto[] }> = []
  for (const item of data ?? []) {
    const group = groups.find((g) => g.queryId === item.savedQueryId)
    if (group) group.items.push(item)
    else groups.push({ queryId: item.savedQueryId, name: item.savedQueryName, items: [item] })
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Add a chart</DialogTitle>
          <DialogDescription>Charts from queries you own or that were shared with you.</DialogDescription>
        </DialogHeader>

        {isLoading && (
          <div className="flex items-center justify-center py-8 text-sm text-muted-foreground">
            <Loader2 className="mr-2 size-4 animate-spin" /> Loading…
          </div>
        )}
        {isError && <p className="py-6 text-center text-sm text-destructive">Could not load your charts.</p>}

        {data && (
          <Command className="h-72 rounded-md border border-input">
            <CommandInput placeholder="Search charts or queries…" />
            <CommandList>
              <CommandEmpty>
                {data.length === 0 ? 'No charts yet. Add a chart to a saved query first.' : 'No matching charts.'}
              </CommandEmpty>
              {groups.map((group) => (
                <CommandGroup key={group.queryId} heading={group.name}>
                  {group.items.map((item) => (
                    <CommandItem
                      key={item.visualizationId}
                      value={`${item.savedQueryName} ${item.visualizationName} ${item.visualizationId}`}
                      onSelect={() => onPick(item)}
                    >
                      <span className="min-w-0 flex-1 truncate">{item.visualizationName}</span>
                      <span className="shrink-0 text-xs text-muted-foreground">{typeLabel(item.type)}</span>
                    </CommandItem>
                  ))}
                </CommandGroup>
              ))}
            </CommandList>
          </Command>
        )}
      </DialogContent>
    </Dialog>
  )
}
