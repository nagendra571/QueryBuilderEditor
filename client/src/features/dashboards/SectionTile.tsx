import { ArrowDown, ArrowUp, GripVertical, MoreVertical, Pencil, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { cn } from '@/lib/utils'

/** Title and description stay React text: a heading typed here can never run as markup. */
function Heading({ titleId, title, description }: { titleId: string; title: string; description?: string | null }) {
  return (
    <>
      <h2
        id={titleId}
        className="min-w-0 truncate text-base leading-6 font-bold tracking-[-0.01em] text-foreground"
      >
        {title}
      </h2>
      {description && <p className="min-w-0 shrink-[4] truncate text-xs text-muted-foreground">{description}</p>}
    </>
  )
}

const Rule = () => <span aria-hidden="true" className="min-w-6 flex-1 -translate-y-1 border-t border-[var(--parcel)]" />

export function SectionTile({
  titleId,
  title,
  description,
  heightPx,
  fillParent,
  editing,
  label,
  onEdit,
  onRemove,
  onMove,
}: {
  titleId: string
  title: string
  description?: string | null
  heightPx?: number
  fillParent?: boolean
  editing: boolean
  label: string
  onEdit: () => void
  onRemove: () => void
  onMove: (direction: 'up' | 'down') => void
}) {
  return (
    <section
      aria-labelledby={titleId}
      style={heightPx ? { height: heightPx } : undefined}
      className={cn('min-w-0', fillParent && 'h-full')}
    >
      {editing ? (
        <div className="flex h-10 items-center gap-2 rounded-md border border-dashed border-foreground/25 bg-background px-2">
          <span className="text-muted-foreground" aria-hidden="true">
            <GripVertical className="size-4" />
          </span>
          <div className="flex min-w-0 flex-1 items-baseline gap-3">
            <Heading titleId={titleId} title={title} description={description} />
            <Rule />
          </div>
          <Button variant="ghost" size="icon" className="size-6" aria-label={`Edit ${label}`} onClick={onEdit}>
            <Pencil className="size-3.5" />
          </Button>
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" className="size-6" aria-label={`Layout of ${label}`}>
                <MoreVertical className="size-3.5" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-44">
              {/* The menu stays open after a move so a keyboard user can press it again. */}
              <DropdownMenuItem
                onSelect={(event) => {
                  event.preventDefault()
                  onMove('up')
                }}
              >
                <ArrowUp className="size-3.5" />
                Move up
              </DropdownMenuItem>
              <DropdownMenuItem
                onSelect={(event) => {
                  event.preventDefault()
                  onMove('down')
                }}
              >
                <ArrowDown className="size-3.5" />
                Move down
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem variant="destructive" onSelect={onRemove}>
                <Trash2 className="size-3.5" />
                Remove section
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      ) : (
        <div className="flex h-full items-baseline gap-3 pt-3.5">
          <Heading titleId={titleId} title={title} description={description} />
          <Rule />
        </div>
      )}
    </section>
  )
}
