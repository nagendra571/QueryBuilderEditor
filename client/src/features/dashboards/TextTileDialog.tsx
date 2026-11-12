import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'

export const MAX_TEXT = 2000

export function TextTileDialog({
  open,
  onOpenChange,
  initialTitle,
  initialText,
  onSave,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  initialTitle: string
  initialText: string
  onSave: (title: string, text: string) => void
}) {
  // The dialog stays mounted between uses; re-key the form so each open starts from the tile's own text.
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        {open && (
          <TextTileForm
            initialTitle={initialTitle}
            initialText={initialText}
            onCancel={() => onOpenChange(false)}
            onSave={onSave}
          />
        )}
      </DialogContent>
    </Dialog>
  )
}

function TextTileForm({
  initialTitle,
  initialText,
  onCancel,
  onSave,
}: {
  initialTitle: string
  initialText: string
  onCancel: () => void
  onSave: (title: string, text: string) => void
}) {
  const [title, setTitle] = useState(initialTitle)
  const [text, setText] = useState(initialText)
  const valid = text.trim().length > 0 && text.length <= MAX_TEXT

  return (
    <>
      <DialogHeader>
        <DialogTitle>Text note</DialogTitle>
        <DialogDescription>Plain text only. Line breaks are kept; nothing is formatted or run.</DialogDescription>
      </DialogHeader>
      <div className="flex flex-col gap-3">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="text-tile-title">Heading (optional)</Label>
          <Input id="text-tile-title" value={title} maxLength={200} onChange={(e) => setTitle(e.target.value)} />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="text-tile-text">Note</Label>
          <Textarea
            id="text-tile-text"
            value={text}
            rows={7}
            maxLength={MAX_TEXT}
            onChange={(e) => setText(e.target.value)}
            autoFocus
          />
          <span className="self-end text-xs text-muted-foreground">
            {text.length} / {MAX_TEXT}
          </span>
        </div>
      </div>
      <DialogFooter>
        <Button variant="outline" onClick={onCancel}>
          Cancel
        </Button>
        <Button disabled={!valid} onClick={() => onSave(title.trim(), text)}>
          Done
        </Button>
      </DialogFooter>
    </>
  )
}
