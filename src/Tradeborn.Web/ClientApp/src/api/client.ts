import type { CityDto } from '@/game/types'

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly code?: string,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

/**
 * The access token is held in a module variable — never localStorage or sessionStorage.
 *
 * ADR-007: a token in web storage turns any XSS into a durable account compromise. In
 * memory it dies with the tab and is only valid for 15 minutes. The refresh token lives in
 * an HttpOnly cookie that JavaScript cannot read at all.
 */
let accessToken: string | null = null

export function setAccessToken(token: string | null): void {
  accessToken = token
}

export function hasAccessToken(): boolean {
  return accessToken !== null
}

interface ProblemDetails {
  title?: string
  detail?: string
  code?: string
}

async function request<T>(path: string, init: RequestInit = {}, retryOn401 = true): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (init.body) headers.set('Content-Type', 'application/json')
  if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)

  const response = await fetch(path, { ...init, headers, credentials: 'same-origin' })

  // A 401 usually just means the 15-minute access token expired. Refresh once and retry,
  // so a long session never bounces the player back to a login screen mid-play.
  if (response.status === 401 && retryOn401 && path !== '/api/auth/refresh') {
    const refreshed = await tryRefresh()
    if (refreshed) {
      return request<T>(path, init, false)
    }
  }

  if (!response.ok) {
    let problem: ProblemDetails = {}
    try {
      problem = (await response.json()) as ProblemDetails
    } catch {
      // Not a problem+json body — fall back to the status text below.
    }
    throw new ApiError(
      problem.detail ?? problem.title ?? `Request failed with ${response.status}`,
      response.status,
      problem.code,
    )
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

interface AuthResponse {
  accessToken: string
  playerId: string
}

export async function register(email: string, password: string, displayName: string): Promise<void> {
  const result = await request<AuthResponse>('/api/auth/register', {
    method: 'POST',
    body: JSON.stringify({ email, password, displayName }),
  })
  setAccessToken(result.accessToken)
}

export async function login(email: string, password: string): Promise<void> {
  const result = await request<AuthResponse>('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify({ email, password }),
  })
  setAccessToken(result.accessToken)
}

export async function logout(): Promise<void> {
  try {
    await request<void>('/api/auth/logout', { method: 'POST' }, false)
  } finally {
    setAccessToken(null)
  }
}

/**
 * Attempts to restore a session from the refresh cookie.
 *
 * Called once on page load: the SPA holds no access token after a refresh, so it asks the
 * server whether the HttpOnly cookie still represents a valid session. This is why
 * reloading the page does not show a login prompt.
 */
export async function tryRefresh(): Promise<boolean> {
  try {
    const result = await request<AuthResponse>('/api/auth/refresh', { method: 'POST' }, false)
    setAccessToken(result.accessToken)
    return true
  } catch {
    setAccessToken(null)
    return false
  }
}

export function fetchCity(signal?: AbortSignal): Promise<CityDto> {
  // No player id in the path: the server resolves the city from the token, which is what
  // makes cross-tenant access structurally impossible (SECURITY_MODEL.md T7).
  return request<CityDto>('/api/cities/me', { signal })
}
