import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { useDataSources } from '@/hooks/useDataSources'

export function NewQueryDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { data: dataSources } = useDataSources()
  const [selected, setSelected] = useState<string>('')
  const navigate = useNavigate()

  const effectiveSelection = selected || dataSources?.[0]?.id || ''

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle>New query</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-1.5">
          <span className="text-xs font-medium text-muted-foreground">Data source</span>
          <Select value={effectiveSelection} onValueChange={setSelected}>
            <SelectTrigger className="w-full">
              <SelectValue placeholder="Choose a data source" />
            </SelectTrigger>
            <SelectContent>
              {dataSources?.map((ds) => (
                <SelectItem key={ds.id} value={ds.id}>
                  {ds.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button disabled={!effectiveSelection} onClick={() => navigate(`/queries/new?dataSourceId=${effectiveSelection}`)}>
            Continue
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
