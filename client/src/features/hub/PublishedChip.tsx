import { Globe } from 'lucide-react'
import { Badge } from '@/components/ui/badge'

export function PublishedChip() {
  return (
    <Badge variant="outline" className="h-4 gap-0.5 px-1 text-[9px] font-medium">
      <Globe className="size-2.5" aria-hidden />
      Published
    </Badge>
  )
}
