import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'

export function SaveQueryDialog({
  open,
  onOpenChange,
  title = 'Save query',
  initialName,
  initialDescription,
  isSaving,
  onConfirm,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title?: string
  initialName: string
  initialDescription: string
  isSaving: boolean
  onConfirm: (name: string, description: string) => void
}) {
  const [name, setName] = useState(initialName)
  const [description, setDescription] = useState(initialDescription)

  // Re-sync from the caller's current values each time the dialog opens, rather than only once at
  // mount — this dialog stays mounted across opens, and its initial values can change between them
  // (e.g. "Save a copy" wants a different starting name than a normal save).
  useEffect(() => {
    if (open) {
      setName(initialName)
      setDescription(initialDescription)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-3">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="query-name">Name</Label>
            <Input id="query-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Active customers by lifetime value" autoFocus />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="query-description">Description (optional)</Label>
            <Textarea id="query-description" value={description} onChange={(e) => setDescription(e.target.value)} rows={3} />
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button disabled={!name.trim() || isSaving} onClick={() => onConfirm(name.trim(), description.trim())}>
            {isSaving ? 'Saving…' : 'Save'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
