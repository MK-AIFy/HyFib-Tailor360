import { useEffect, useState } from 'react'

/** The anonymous endpoint the shell reads on start-up (src/Hosts/Tailor360.Web VersionEndpoints). */
export const VERSION_ENDPOINT = '/api/version'

/**
 * The build, schema and compatibility description returned by GET /api/version.
 *
 * It is the one endpoint that describes the server rather than answering a question about the caller,
 * and the shell reads it before a session exists: the training banner, the "an update is ready" prompt
 * of #51 and the refusal to run against an API major this build was not written for all come from here.
 */
export interface VersionInfo {
  /** The major API surface the server serves, for example `v1`. */
  readonly api: string
  /**
   * The oldest client build the server answers, or empty when it answers every build.
   *
   * A build below it is refused with 426, so a client at or below it prompts for an update rather than
   * waiting to be refused mid-task.
   */
  readonly minimumClient: string
  /** The build the server is serving. A different value here means a newer client is available. */
  readonly current: string
  /** The environment name, lower case. Anything but `production` shows the training banner. */
  readonly environment: string
  /**
   * The newest migration timestamp the server's build carries. It changes only when the database shape
   * changes, which is how a client with cached payloads tells a routine deployment from one that may
   * have changed what those payloads mean.
   */
  readonly schemaVersion: string
  /** The short source revision. Present in development only, so every consumer treats it as optional. */
  readonly commit?: string
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
    typeof candidate['api'] === 'string' &&
    typeof candidate['minimumClient'] === 'string' &&
    typeof candidate['current'] === 'string' &&
    typeof candidate['environment'] === 'string' &&
    typeof candidate['schemaVersion'] === 'string' &&
    // Absent outside development, and never anything but a string when it is there.
    (candidate['commit'] === undefined || typeof candidate['commit'] === 'string')
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
    api: payload.api,
    minimumClient: payload.minimumClient,
    current: payload.current,
    environment: payload.environment,
    schemaVersion: payload.schemaVersion,
    ...(payload.commit === undefined ? {} : { commit: payload.commit }),
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
