import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import type { AddToQueryPlan } from '@/features/builder/results/filters-to-query'

export function AddToQueryDialog({
  plan,
  onConfirm,
  onCancel,
  onCloseAutoFocus,
}: {
  onCloseAutoFocus?: (e: Event) => void
  /** Null = closed. */
  plan: AddToQueryPlan | null
  onConfirm: () => void
  onCancel: () => void
}) {
  const where = plan?.add.filter((p) => p.clause === 'where') ?? []
  const having = plan?.add.filter((p) => p.clause === 'having') ?? []
  const k = plan?.add.length ?? 0
  const title = k === 0 ? "These filters can't be added to the query" : `Add ${k} ${k === 1 ? 'filter' : 'filters'} to the query?`

  return (
    <Dialog open={plan !== null} onOpenChange={(open) => !open && onCancel()}>
      <DialogContent className="max-h-[80vh] overflow-y-auto sm:max-w-md" onCloseAutoFocus={onCloseAutoFocus}>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription className="sr-only">Review what will be added to the query before it runs again.</DialogDescription>
        </DialogHeader>
        {plan && (
          <div className="space-y-3 text-sm">
            {where.length > 0 && (
              <section>
                <h3 className="mb-1 font-medium">Filters</h3>
                <ul className="list-disc space-y-0.5 pl-5">
                  {where.map((p) => (
                    <li key={p.column}>{p.description}</li>
                  ))}
                </ul>
              </section>
            )}
            {having.length > 0 && (
              <section>
                <h3 className="mb-1 font-medium">Totals filters</h3>
                <ul className="list-disc space-y-0.5 pl-5">
                  {having.map((p) => (
                    <li key={p.column}>{p.description}</li>
                  ))}
                </ul>
              </section>
            )}
            {plan.blocked.length > 0 && (
              <section>
                <h3 className="mb-1 font-medium">Can't be added</h3>
                <ul className="list-disc space-y-0.5 pl-5">
                  {plan.blocked.map((b) => (
                    <li key={b.column}>
                      {b.reason}
                    </li>
                  ))}
                </ul>
              </section>
            )}
            {plan.warnings.map((w) => (
              <p key={w} className="text-muted-foreground">
                {w}
              </p>
            ))}
          </div>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={onCancel}>
            Cancel
          </Button>
          <Button onClick={onConfirm} disabled={k === 0}>
            Add and run
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
