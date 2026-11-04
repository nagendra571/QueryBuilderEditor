import { createContext, useContext } from 'react'

/** Runs at most `max` async jobs at once; the rest wait their turn in arrival order. */
export function createLimiter(max: number) {
  let running = 0
  const waiting: Array<() => void> = []

  function release() {
    running -= 1
    waiting.shift()?.()
  }

  return function limit<T>(job: () => Promise<T>): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      const start = () => {
        running += 1
        job().then(resolve, reject).finally(release)
      }
      if (running < max) start()
      else waiting.push(start)
    })
  }
}

export type Limiter = ReturnType<typeof createLimiter>

// Tiles outside a dashboard page (none today) would run unthrottled.
export const RenderLimiterContext = createContext<Limiter>((job) => job())

export function useRenderLimiter() {
  return useContext(RenderLimiterContext)
}
