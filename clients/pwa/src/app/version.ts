import { useEffect, useState } from 'react'

/** The anonymous endpoint the shell reads on start-up (src/Hosts/Tailor360.Web VersionEndpoints). */
export const VERSION_ENDPOINT = '/api/version'

/** The build and environment description returned by GET /api/version. */
export interface VersionInfo {
  readonly version: string
  readonly buildHash: string
  readonly environment: string
}

export type VersionState =
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly info: VersionInfo }
  | { readonly status: 'error' }

function isVersionInfo(payload: unknown): payload is VersionInfo {
  if (typeof payload !== 'object' || payload === null) {
    return false
  }
  const candidate = payload as Record<string, unknown>
  return (
    typeof candidate['version'] === 'string' &&
    typeof candidate['buildHash'] === 'string' &&
    typeof candidate['environment'] === 'string'
  )
}

/**
 * Reads GET /api/version. The response is validated rather than trusted, because a reverse proxy or a
 * stale service worker can return an HTML error page with a 200.
 */
export async function fetchVersion(signal?: AbortSignal): Promise<VersionInfo> {
  const response = await fetch(VERSION_ENDPOINT, {
    headers: { Accept: 'application/json' },
    credentials: 'same-origin',
    ...(signal ? { signal } : {}),
  })

  if (!response.ok) {
    throw new Error(`GET ${VERSION_ENDPOINT} returned ${String(response.status)}`)
  }

  const payload: unknown = await response.json()
  if (!isVersionInfo(payload)) {
    throw new Error(`GET ${VERSION_ENDPOINT} returned an unexpected payload`)
  }

  return {
    version: payload.version,
    buildHash: payload.buildHash,
    environment: payload.environment,
  }
}

/**
 * True only for the production environment. Every other environment — development, test, staging,
 * training — must show the training banner, so the check is deliberately an equality test against
 * 'production' and not a list of non-production names: an unknown name is treated as not production.
 */
export function isProductionEnvironment(environment: string): boolean {
  return environment.trim().toLowerCase() === 'production'
}

/**
 * Fetches the version once per mount.
 *
 * #50 replaces this with the shared TanStack Query cache so that several consumers cause one request;
 * until then each caller issues its own, which is acceptable for a single cheap anonymous GET.
 */
export function useVersion(): VersionState {
  const [state, setState] = useState<VersionState>({ status: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    let cancelled = false

    void fetchVersion(controller.signal)
      .then((info) => {
        if (!cancelled) {
          setState({ status: 'ready', info })
        }
      })
      .catch(() => {
        if (!cancelled) {
          setState({ status: 'error' })
        }
      })

    return () => {
      cancelled = true
      controller.abort()
    }
  }, [])

  return state
}
