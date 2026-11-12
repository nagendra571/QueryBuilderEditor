import { Check, LayoutDashboard, Loader2, Plus } from 'lucide-react'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { useAddChartToDashboard, useCreateDashboard, useDashboards } from '@/hooks/useDashboards'
import type { VisualizationDto } from '@/types'

export function AddToDashboardDialog({
  open,
  onOpenChange,
  visualization,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  visualization: VisualizationDto
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        {open && <Picker visualization={visualization} />}
      </DialogContent>
    </Dialog>
  )
}

function Picker({ visualization }: { visualization: VisualizationDto }) {
  const navigate = useNavigate()
  const { data, isLoading, isError } = useDashboards()
  const addChart = useAddChartToDashboard()
  const createDashboard = useCreateDashboard()
  const [added, setAdded] = useState<string[]>([])
  const [newName, setNewName] = useState('')

  const editable = (data ?? []).filter((d) => d.accessLevel === 'owner' || d.accessLevel === 'editor')
  const busy = addChart.isPending || createDashboard.isPending

  function add(dashboardId: string, dashboardName: string) {
    addChart.mutate(
      { dashboardId, visualizationId: visualization.id },
      {
        onSuccess: () => {
          setAdded((current) => [...current, dashboardId])
          toast.success(`Added "${visualization.name}" to ${dashboardName}.`, {
            action: { label: 'Open', onClick: () => navigate(`/dashboards/${dashboardId}`) },
          })
        },
      },
    )
  }

  function createAndAdd() {
    const name = newName.trim()
    if (!name) return
    createDashboard.mutate(
      { name, description: null, version: null },
      {
        onSuccess: (created) => {
          setNewName('')
          add(created.id, created.name)
        },
      },
    )
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Add to dashboard</DialogTitle>
        <DialogDescription>
          Adds “{visualization.name}” as a new tile at the end. Everyone who opens the dashboard sees only the rows
          they are allowed to see.
        </DialogDescription>
      </DialogHeader>

      {isLoading && (
        <div className="flex items-center justify-center py-8 text-sm text-muted-foreground">
          <Loader2 className="mr-2 size-4 animate-spin" /> Loading…
        </div>
      )}
      {isError && <p className="py-6 text-center text-sm text-destructive">Could not load your dashboards.</p>}

      {data && (
        <Command className="h-60 rounded-md border border-input">
          <CommandInput placeholder="Search dashboards…" />
          <CommandList>
            <CommandEmpty>
              {editable.length === 0 ? 'No dashboards you can edit yet. Create one below.' : 'No matching dashboards.'}
            </CommandEmpty>
            <CommandGroup>
              {editable.map((d) => {
                const done = added.includes(d.id)
                return (
                  <CommandItem
                    key={d.id}
                    value={`${d.name} ${d.id}`}
                    disabled={busy}
                    onSelect={() => add(d.id, d.name)}
                  >
                    <LayoutDashboard className="size-4 shrink-0 text-muted-foreground" />
                    <span className="min-w-0 flex-1 truncate">{d.name}</span>
                    {done ? (
                      <span className="flex shrink-0 items-center gap-1 text-xs text-muted-foreground">
                        <Check className="size-3.5" /> Added
                      </span>
                    ) : (
                      <span className="shrink-0 text-xs text-muted-foreground">
                        {d.tileCount} {d.tileCount === 1 ? 'tile' : 'tiles'}
                        {d.isOwner ? '' : ` · ${d.ownerName}`}
                      </span>
                    )}
                  </CommandItem>
                )
              })}
            </CommandGroup>
          </CommandList>
        </Command>
      )}

      <form
        className="flex items-center gap-2"
        onSubmit={(e) => {
          e.preventDefault()
          createAndAdd()
        }}
      >
        <Input
          value={newName}
          onChange={(e) => setNewName(e.target.value)}
          placeholder="New dashboard name"
          aria-label="New dashboard name"
          maxLength={200}
          className="h-9"
        />
        <Button type="submit" size="sm" className="h-9 shrink-0" disabled={busy || !newName.trim()}>
          {createDashboard.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Plus className="size-3.5" />}
          Create and add
        </Button>
      </form>
    </>
  )
}
