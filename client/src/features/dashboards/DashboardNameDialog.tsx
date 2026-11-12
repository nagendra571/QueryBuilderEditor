import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

/** Create or rename: one name field. */
export function DashboardNameDialog({
  open,
  onOpenChange,
  title,
  confirmLabel,
  initialName,
  pending,
  onConfirm,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  confirmLabel: string
  initialName: string
  pending: boolean
  onConfirm: (name: string) => void
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        {open && (
          <NameForm
            title={title}
            confirmLabel={confirmLabel}
            initialName={initialName}
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
  pending,
  onCancel,
  onConfirm,
}: {
  title: string
  confirmLabel: string
  initialName: string
  pending: boolean
  onCancel: () => void
  onConfirm: (name: string) => void
}) {
  const [name, setName] = useState(initialName)
  const valid = name.trim().length > 0

  return (
    <form
      className="flex flex-col gap-4"
      onSubmit={(e) => {
        e.preventDefault()
        if (valid && !pending) onConfirm(name.trim())
      }}
    >
      <DialogHeader>
        <DialogTitle>{title}</DialogTitle>
      </DialogHeader>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="dashboard-name">Name</Label>
        <Input id="dashboard-name" value={name} maxLength={200} autoFocus onChange={(e) => setName(e.target.value)} />
      </div>
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
