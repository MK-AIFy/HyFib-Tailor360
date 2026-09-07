/**
 * The browser half of the WebAuthn ceremonies.
 *
 * The server speaks the W3C options object; the browser speaks `ArrayBuffer`s. Everything in this
 * file exists to cross that gap without either side having to know about the other, and it is the
 * only module in the application that touches `navigator.credentials`.
 *
 * ## Three things here are load-bearing
 *
 *  1. **Transports are filtered to the six the server understands.** `getTransports()` may report a
 *     value the relying-party library does not know — Chrome still reports the legacy `cable` on some
 *     Android flows — and the .NET side deserialises the transport list into a closed enumeration.
 *     One unrecognised string makes the whole registration fail with a 400 that says nothing useful.
 *     Sending only the recognised ones costs nothing: the list is a hint about how to reach the
 *     authenticator next time, and a shorter accurate hint is better than a rejected registration.
 *  2. **A cancelled ceremony is not a failure.** Dismissing the platform's own dialog throws
 *     `NotAllowedError`, and so does a genuine refusal; treating either as an error puts a red banner
 *     on the screen of somebody who simply changed their mind. `PasskeyCancelled` separates them so
 *     the screen can say "nothing has changed" instead.
 *  3. **Nothing here is stored.** The credential the authenticator returns is posted straight to the
 *     server and dropped. The private key never leaves the authenticator, which is the entire point
 *     of the mechanism, and the client keeps no copy of anything it saw.
 */

/** The transports the relying party accepts. Anything else is dropped rather than sent. */
const KNOWN_TRANSPORTS: readonly string[] = [
  'usb',
  'nfc',
  'ble',
  'smart-card',
  'hybrid',
  'internal',
]

/** Thrown when the person dismissed the platform dialog. Not an error worth a banner. */
export class PasskeyCancelled extends Error {
  constructor() {
    super('The passkey ceremony was cancelled.')
    this.name = 'PasskeyCancelled'
  }
}

/** Thrown when this browser cannot do WebAuthn at all. */
export class PasskeyUnsupported extends Error {
  constructor() {
    super('This browser does not support passkeys.')
    this.name = 'PasskeyUnsupported'
  }
}

/**
 * Whether this browser can run a ceremony.
 *
 * Checked before the control is offered rather than after it fails: a "Sign in with a passkey" button
 * that always throws is worse than no button, especially on the older Android WebView a shop may
 * still have on a counter device.
 */
export function isPasskeySupported(): boolean {
  return (
    typeof globalThis.PublicKeyCredential !== 'undefined' &&
    typeof navigator !== 'undefined' &&
    navigator.credentials !== undefined &&
    typeof navigator.credentials.create === 'function'
  )
}

/**
 * base64url (no padding) to bytes.
 *
 * The buffer is allocated explicitly rather than by `new Uint8Array(length)`, so the result is a
 * `Uint8Array<ArrayBuffer>` rather than a `Uint8Array<ArrayBufferLike>`. Only the first satisfies
 * `BufferSource`, which is what every field of a WebAuthn options object is typed as: the second
 * could in principle be backed by a `SharedArrayBuffer`, which no authenticator will accept.
 */
export function base64UrlToBytes(value: string): Uint8Array<ArrayBuffer> {
  const padded = value.replace(/-/g, '+').replace(/_/g, '/')
  const complete = padded.padEnd(padded.length + ((4 - (padded.length % 4)) % 4), '=')
  const binary = atob(complete)
  const bytes = new Uint8Array(new ArrayBuffer(binary.length))
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index)
  }
  return bytes
}

/** Bytes to base64url (no padding), which is what every field in the contract uses. */
export function bytesToBase64Url(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer)
  let binary = ''
  for (const byte of bytes) {
    binary += String.fromCharCode(byte)
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

function record(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : {}
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

function descriptors(value: unknown): PublicKeyCredentialDescriptor[] {
  if (!Array.isArray(value)) {
    return []
  }
  return value.map((entry) => {
    const item = record(entry)
    const transports = Array.isArray(item['transports'])
      ? (item['transports'] as unknown[]).filter(
          (candidate): candidate is AuthenticatorTransport =>
            typeof candidate === 'string' && KNOWN_TRANSPORTS.includes(candidate),
        )
      : undefined
    return {
      type: 'public-key' as const,
      id: base64UrlToBytes(text(item['id'])),
      ...(transports === undefined || transports.length === 0 ? {} : { transports }),
    }
  })
}

/** Turns the server's creation options into what `navigator.credentials.create` expects. */
export function toCreationOptions(options: unknown): PublicKeyCredentialCreationOptions {
  const source = record(options)
  const user = record(source['user'])

  return {
    rp: source['rp'] as PublicKeyCredentialRpEntity,
    user: {
      id: base64UrlToBytes(text(user['id'])),
      name: text(user['name']),
      displayName: text(user['displayName']),
    },
    challenge: base64UrlToBytes(text(source['challenge'])),
    pubKeyCredParams: (source['pubKeyCredParams'] ?? []) as PublicKeyCredentialParameters[],
    ...(typeof source['timeout'] === 'number' ? { timeout: source['timeout'] } : {}),
    ...(typeof source['attestation'] === 'string'
      ? { attestation: source['attestation'] as AttestationConveyancePreference }
      : {}),
    ...(source['authenticatorSelection'] === undefined
      ? {}
      : {
          authenticatorSelection: source[
            'authenticatorSelection'
          ] as AuthenticatorSelectionCriteria,
        }),
    excludeCredentials: descriptors(source['excludeCredentials']),
  }
}

/** Turns the server's assertion options into what `navigator.credentials.get` expects. */
export function toRequestOptions(options: unknown): PublicKeyCredentialRequestOptions {
  const source = record(options)

  return {
    challenge: base64UrlToBytes(text(source['challenge'])),
    ...(typeof source['timeout'] === 'number' ? { timeout: source['timeout'] } : {}),
    ...(typeof source['rpId'] === 'string' ? { rpId: source['rpId'] } : {}),
    // Deliberately empty for a discoverable-credential sign-in: the authenticator offers whichever
    // credentials it holds for this relying party, so nobody has to type a name first.
    allowCredentials: descriptors(source['allowCredentials']),
    ...(typeof source['userVerification'] === 'string'
      ? { userVerification: source['userVerification'] as UserVerificationRequirement }
      : {}),
  }
}

function isCancellation(cause: unknown): boolean {
  return (
    cause instanceof DOMException &&
    (cause.name === 'NotAllowedError' || cause.name === 'AbortError')
  )
}

/** Runs the registration ceremony and returns the credential in the shape the server reads. */
export async function createPasskey(options: unknown): Promise<unknown> {
  if (!isPasskeySupported()) {
    throw new PasskeyUnsupported()
  }

  let credential: Credential | null
  try {
    credential = await navigator.credentials.create({ publicKey: toCreationOptions(options) })
  } catch (cause) {
    throw isCancellation(cause) ? new PasskeyCancelled() : cause
  }

  if (credential === null) {
    throw new PasskeyCancelled()
  }

  const created = credential as PublicKeyCredential
  const response = created.response as AuthenticatorAttestationResponse
  const transports =
    typeof response.getTransports === 'function'
      ? response.getTransports().filter((value) => KNOWN_TRANSPORTS.includes(value))
      : []

  return {
    id: created.id,
    rawId: bytesToBase64Url(created.rawId),
    type: created.type,
    response: {
      attestationObject: bytesToBase64Url(response.attestationObject),
      clientDataJSON: bytesToBase64Url(response.clientDataJSON),
      ...(transports.length === 0 ? {} : { transports }),
    },
    clientExtensionResults: created.getClientExtensionResults(),
  }
}

/** Runs the sign-in ceremony and returns the assertion in the shape the server reads. */
export async function assertPasskey(options: unknown): Promise<unknown> {
  if (!isPasskeySupported()) {
    throw new PasskeyUnsupported()
  }

  let credential: Credential | null
  try {
    credential = await navigator.credentials.get({ publicKey: toRequestOptions(options) })
  } catch (cause) {
    throw isCancellation(cause) ? new PasskeyCancelled() : cause
  }

  if (credential === null) {
    throw new PasskeyCancelled()
  }

  const asserted = credential as PublicKeyCredential
  const response = asserted.response as AuthenticatorAssertionResponse

  return {
    id: asserted.id,
    rawId: bytesToBase64Url(asserted.rawId),
    type: asserted.type,
    response: {
      authenticatorData: bytesToBase64Url(response.authenticatorData),
      signature: bytesToBase64Url(response.signature),
      clientDataJSON: bytesToBase64Url(response.clientDataJSON),
      ...(response.userHandle === null
        ? {}
        : { userHandle: bytesToBase64Url(response.userHandle) }),
    },
    clientExtensionResults: asserted.getClientExtensionResults(),
  }
}
