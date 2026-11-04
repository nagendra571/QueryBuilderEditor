import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { MAX_SECTION_DESCRIPTION, MAX_SECTION_TITLE, validateSection } from '@/features/dashboards/section'

export function SectionTileDialog({
  open,
  onOpenChange,
  initialTitle,
  initialDescription,
  onSave,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  initialTitle: string
  initialDescription: string
  onSave: (title: string, description: string) => void
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        {open && (
          <SectionForm
            initialTitle={initialTitle}
            initialDescription={initialDescription}
            onCancel={() => onOpenChange(false)}
            onSave={onSave}
          />
        )}
      </DialogContent>
    </Dialog>
  )
}

function SectionForm({
  initialTitle,
  initialDescription,
  onCancel,
  onSave,
}: {
  initialTitle: string
  initialDescription: string
  onCancel: () => void
  onSave: (title: string, description: string) => void
}) {
  const [title, setTitle] = useState(initialTitle)
  const [description, setDescription] = useState(initialDescription)
  const check = validateSection(title, description)

  return (
    <form
      className="contents"
      onSubmit={(e) => {
        e.preventDefault()
        if (check.valid) onSave(title.trim(), description.trim())
      }}
    >
      <DialogHeader>
        <DialogTitle>Section</DialogTitle>
        <DialogDescription>A heading that groups the tiles below it. Plain text, shown full width.</DialogDescription>
      </DialogHeader>
      <div className="flex flex-col gap-3">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="section-title">Title</Label>
          <Input
            id="section-title"
            value={title}
            maxLength={MAX_SECTION_TITLE}
            onChange={(e) => setTitle(e.target.value)}
            autoFocus
          />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="section-description">Description (optional)</Label>
          <Input
            id="section-description"
            value={description}
            maxLength={MAX_SECTION_DESCRIPTION}
            aria-invalid={!!check.descriptionError}
            onChange={(e) => setDescription(e.target.value)}
          />
          <span className="self-end text-xs text-muted-foreground">
            {description.length} / {MAX_SECTION_DESCRIPTION}
          </span>
        </div>
      </div>
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" disabled={!check.valid}>
          Done
        </Button>
      </DialogFooter>
    </form>
  )
}
