import { ContextMenu, ContextMenuContent, ContextMenuItem, ContextMenuSeparator, ContextMenuTrigger } from '@/components/ui/context-menu'
import { hideText, showOnlyText } from './filter-actions.ts'

interface Props {
  children: React.ReactElement
  /** Raw value of the cell the menu was opened on; null until a cell has been targeted. */
  value: unknown
  hasTarget: boolean
  onClose: () => void
  onShowOnly: () => void
  onHide: () => void
  onCopyCell: () => void
  onOpenRecord?: () => void
}

export function CellContextMenu({ children, value, hasTarget, onClose, onShowOnly, onHide, onCopyCell, onOpenRecord }: Props) {
  return (
    <ContextMenu onOpenChange={(open) => !open && onClose()}>
      <ContextMenuTrigger asChild>{children}</ContextMenuTrigger>
      {hasTarget && (
        <ContextMenuContent onContextMenu={(e) => e.preventDefault()}>
          <ContextMenuItem onSelect={onShowOnly}>{showOnlyText(value)}</ContextMenuItem>
          <ContextMenuItem onSelect={onHide}>{hideText(value)}</ContextMenuItem>
          <ContextMenuSeparator />
          <ContextMenuItem onSelect={onCopyCell}>Copy cell</ContextMenuItem>
          {onOpenRecord && <ContextMenuItem onSelect={onOpenRecord}>Open record</ContextMenuItem>}
        </ContextMenuContent>
      )}
    </ContextMenu>
  )
}
