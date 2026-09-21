import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'

export function SaveQueryDialog({
  open,
  onOpenChange,
  initialName,
  initialDescription,
  isSaving,
  onConfirm,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  initialName: string
  initialDescription: string
  isSaving: boolean
  onConfirm: (name: string, description: string) => void
}) {
  const [name, setName] = useState(initialName)
  const [description, setDescription] = useState(initialDescription)

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Save query</DialogTitle>
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
            {isSaving ? 'Saving…' : 'Save query'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
