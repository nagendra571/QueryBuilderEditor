import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'

/** Create or rename: one name field. */
export function DashboardNameDialog({
  open,
  onOpenChange,
  title,
  confirmLabel,
  initialName,
  initialClickToFilter,
  pending,
  onConfirm,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  confirmLabel: string
  initialName: string
  /** Shows the "Allow click-to-filter" switch when given (a dashboard being edited, not created). */
  initialClickToFilter?: boolean
  pending: boolean
  onConfirm: (name: string, clickToFilter?: boolean) => void
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        {open && (
          <NameForm
            title={title}
            confirmLabel={confirmLabel}
            initialName={initialName}
            initialClickToFilter={initialClickToFilter}
            pending={pending}
            onCancel={() => onOpenChange(false)}
            onConfirm={onConfirm}
          />
        )}
      </DialogContent>
    </Dialog>
  )
}

function NameForm({
  title,
  confirmLabel,
  initialName,
  initialClickToFilter,
  pending,
  onCancel,
  onConfirm,
}: {
  title: string
  confirmLabel: string
  initialName: string
  initialClickToFilter?: boolean
  pending: boolean
  onCancel: () => void
  onConfirm: (name: string, clickToFilter?: boolean) => void
}) {
  const [name, setName] = useState(initialName)
  const [clickToFilter, setClickToFilter] = useState(initialClickToFilter ?? false)
  const valid = name.trim().length > 0

  return (
    <form
      className="flex flex-col gap-4"
      onSubmit={(e) => {
        e.preventDefault()
        if (valid && !pending) onConfirm(name.trim(), initialClickToFilter === undefined ? undefined : clickToFilter)
      }}
    >
      <DialogHeader>
        <DialogTitle>{title}</DialogTitle>
      </DialogHeader>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="dashboard-name">Name</Label>
        <Input id="dashboard-name" value={name} maxLength={200} autoFocus onChange={(e) => setName(e.target.value)} />
      </div>
      {initialClickToFilter !== undefined && (
        <div className="flex items-start gap-3">
          <Switch id="dashboard-click-to-filter" checked={clickToFilter} onCheckedChange={setClickToFilter} />
          <div className="flex flex-col gap-0.5">
            <Label htmlFor="dashboard-click-to-filter">Allow click-to-filter</Label>
            <p className="text-xs text-muted-foreground">
              Viewers can click a mark in one chart and narrow the other charts to that value. Each chart still shows only its own zone.
            </p>
          </div>
        </div>
      )}
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" disabled={!valid || pending}>
          {confirmLabel}
        </Button>
      </DialogFooter>
    </form>
  )
}
