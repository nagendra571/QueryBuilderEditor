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
  (error) => {
    if (error.response?.data?.title) {
      return Promise.reject(new ApiError(error.response.data as ApiProblemDetails))
    }
    return Promise.reject(error)
  },
)
