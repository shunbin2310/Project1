import { getAccessToken } from '@/services/authSession'

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5165'

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

export interface ApiRequestOptions extends RequestInit {
  authenticated?: boolean
  handleUnauthorized?: boolean
}

type ApiErrorConstructor = new (status: number, message: string) => ApiError
type UnauthorizedHandler = () => void | Promise<void>

let unauthorizedHandler: UnauthorizedHandler | null = null

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

export function setUnauthorizedHandler(handler: UnauthorizedHandler | null) {
  unauthorizedHandler = handler
}

export async function apiRequest<T>(
  path: string,
  options: ApiRequestOptions = {},
  ErrorType: ApiErrorConstructor = ApiError,
): Promise<T> {
  const {
    authenticated = true,
    handleUnauthorized = true,
    headers: requestHeaders,
    ...fetchOptions
  } = options
  const headers: Record<string, string> = {}
  new Headers(requestHeaders).forEach((value, key) => {
    headers[key] = value
  })
  headers.Accept = 'application/json'

  const accessToken = authenticated ? getAccessToken() : null
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`

  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...fetchOptions,
    headers,
  })

  if (!response.ok) {
    let message = `Request failed with status ${response.status}`

    try {
      const problem = (await response.json()) as ProblemDetails
      const validationMessage = problem.errors
        ? Object.values(problem.errors).flat().join(' ')
        : undefined

      message = validationMessage || problem.detail || problem.title || message
    } catch {
      // The response did not include a JSON error body.
    }

    if (response.status === 401 && handleUnauthorized && unauthorizedHandler) {
      await unauthorizedHandler()
    }

    throw new ErrorType(response.status, message)
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}
