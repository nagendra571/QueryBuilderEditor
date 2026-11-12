import { Lock } from 'lucide-react'
import { Link } from 'react-router-dom'

export function NotAvailablePage() {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-3 p-6 text-center">
      <Lock className="size-8 text-muted-foreground" aria-hidden />
      <div>
        <h1 className="text-lg font-semibold">Not available for your role</h1>
        <p className="text-sm text-muted-foreground">Your role in this app does not include this page.</p>
      </div>
      <Link to="/dashboards" className="text-sm underline underline-offset-2">
        Go to dashboards
      </Link>
    </div>
  )
}
