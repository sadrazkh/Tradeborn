import type { CityDto } from '@/game/types'

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(url, {
    headers: { Accept: 'application/json' },
    signal,
  })

  if (!response.ok) {
    throw new ApiError(`Request to ${url} failed with ${response.status}`, response.status)
  }

  return (await response.json()) as T
}

/**
 * Phase 0 prototype endpoint. The world layout comes from the server even now, so the
 * renderer is never written against client-side truth (SECURITY_MODEL.md §3).
 * Replaced by the real Cities endpoint in Phase 1.
 */
export function fetchPrototypeCity(signal?: AbortSignal): Promise<CityDto> {
  return getJson<CityDto>('/api/prototype/city', signal)
}
