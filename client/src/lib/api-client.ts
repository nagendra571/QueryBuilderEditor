import axios from 'axios'
import type { ApiProblemDetails } from '@/types'

export const apiClient = axios.create({
  baseURL: '/api',
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
