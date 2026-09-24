import axios from 'axios'
import type { ApiProblemDetails } from '@/types'

// Matches QueryBuilder.Editor's fixed mount path (QueryBuilderRoutes.BasePath) — the API lives
// under the same /querybuilder prefix as the UI so it never collides with a host app's own /api.
export const apiClient = axios.create({
  baseURL: '/querybuilder/api',
  headers: { 'Content-Type': 'application/json' },
})

export class ApiError extends Error {
  status: number
  errors?: Record<string, string[]>

  constructor(problem: ApiProblemDetails) {
    super(problem.detail || problem.title || 'Request failed')
    this.status = problem.status
    this.errors = problem.errors
  }
}

apiClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    let data = error.response?.data
    // Requests made with responseType 'blob' (export) receive their ProblemDetails error body as a
    // Blob too — without parsing it, every export failure would surface as a generic message.
    if (data instanceof Blob && data.type.includes('json')) {
      try {
        data = JSON.parse(await data.text())
      } catch {
        // not a ProblemDetails body after all — fall through to the raw error
      }
    }
    if (data?.title) {
      return Promise.reject(new ApiError(data as ApiProblemDetails))
    }
    return Promise.reject(error)
  },
)
