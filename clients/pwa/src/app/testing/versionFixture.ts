import type { VersionInfo } from '../version'

/**
 * A synthetic `GET /api/version` payload.
 *
 * Every screen that renders the shell reads this endpoint, so the shape is asserted in a dozen tests
 * and would otherwise be spelled out a dozen times. Kept as one helper so a change to the contract
 * fails the type check in one place rather than passing in eleven tests and failing in the twelfth.
 */
export function versionPayload(overrides: Partial<VersionInfo> = {}): VersionInfo {
  return {
    api: 'v1',
    minimumClient: '',
    current: '0.1.0-alpha',
    environment: 'development',
    schemaVersion: '20260101000000',
    commit: '0abcdef',
    ...overrides,
  }
}

/** The same payload as a `Response`, for a `fetch` stub. */
export function versionResponse(overrides: Partial<VersionInfo> = {}): Response {
  return new Response(JSON.stringify(versionPayload(overrides)), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}
