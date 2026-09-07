import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  base64UrlToBytes,
  bytesToBase64Url,
  createPasskey,
  isPasskeySupported,
  PasskeyCancelled,
  PasskeyUnsupported,
  toCreationOptions,
  toRequestOptions,
} from './passkeys'

/** The options object the server actually sends, taken from Fido2.AspNet 4.0.1's own output. */
const CREATION_OPTIONS = {
  rp: { id: 'shop.example', name: 'HyFib Tailor360' },
  user: { name: 'asha.counter', id: 'dXNlci0x', displayName: 'Asha (counter)' },
  challenge: 'FJZSlpiP7Ncg2Uq6ZwE9CKnmjpak6VHp1hYzTe-4J38',
  pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
  timeout: 60000,
  attestation: 'none',
  attestationFormats: [],
  authenticatorSelection: {
    residentKey: 'required',
    requireResidentKey: true,
    userVerification: 'required',
  },
  hints: [],
  excludeCredentials: [{ type: 'public-key', id: 'AQIDBA', transports: ['usb', 'cable'] }],
}

const REQUEST_OPTIONS = {
  challenge: '4JtbQHVErlAN5qMV6zj0v262P-GpfBqGx83wA27IYwc',
  timeout: 60000,
  rpId: 'shop.example',
  allowCredentials: [],
  userVerification: 'required',
  hints: [],
}

function buffer(...bytes: number[]): ArrayBuffer {
  return new Uint8Array(bytes).buffer
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('base64url', () => {
  it('round-trips bytes that need padding and bytes that do not', () => {
    for (const bytes of [[1], [1, 2], [1, 2, 3], [1, 2, 3, 4], [251, 252, 253, 254, 255]]) {
      const encoded = bytesToBase64Url(new Uint8Array(bytes).buffer)
      expect(Array.from(base64UrlToBytes(encoded))).toEqual(bytes)
    }
  })

  it('encodes without padding and without the two characters that are unsafe in a URL', () => {
    const encoded = bytesToBase64Url(buffer(251, 255, 190))
    expect(encoded).not.toContain('=')
    expect(encoded).not.toContain('+')
    expect(encoded).not.toContain('/')
  })

  it('decodes the challenge the server sends, at full length', () => {
    // 32 bytes: the challenge size the relying party is configured with. A decoder that silently
    // truncated would produce a ceremony the authenticator signs and the server rejects.
    expect(base64UrlToBytes(CREATION_OPTIONS.challenge)).toHaveLength(32)
  })
})

describe('turning the server options into what the browser wants', () => {
  it('decodes the challenge and the user handle into buffers', () => {
    const options = toCreationOptions(CREATION_OPTIONS)
    expect(options.challenge).toBeInstanceOf(Uint8Array)
    expect(options.user.id).toBeInstanceOf(Uint8Array)
    expect(options.rp.id).toBe('shop.example')
    expect(options.user.displayName).toBe('Asha (counter)')
  })

  it('drops a transport the relying party does not understand', () => {
    // Chrome still reports the legacy `cable` on some Android flows, and the .NET side deserialises
    // the transport list into a closed enumeration: one unrecognised string fails the whole
    // registration with a 400 that says nothing useful. Filtering is what keeps that from happening.
    const options = toCreationOptions(CREATION_OPTIONS)
    expect(options.excludeCredentials?.[0]?.transports).toEqual(['usb'])
  })

  it('asks for a discoverable credential, because the assertion sends no allow-list', () => {
    const creation = toCreationOptions(CREATION_OPTIONS)
    const request = toRequestOptions(REQUEST_OPTIONS)
    expect(creation.authenticatorSelection?.residentKey).toBe('required')
    expect(request.allowCredentials).toEqual([])
  })

  it('survives an options object with nothing in it', () => {
    const options = toCreationOptions({})
    expect(options.challenge).toHaveLength(0)
    expect(options.pubKeyCredParams).toEqual([])
  })
})

describe('running a registration ceremony', () => {
  function stubAuthenticator(create: () => Promise<Credential | null>) {
    vi.stubGlobal('PublicKeyCredential', function PublicKeyCredentialStub() {
      /* A constructor is all `isPasskeySupported` looks for. */
    })
    vi.stubGlobal('navigator', {
      ...navigator,
      credentials: { create, get: create },
    })
  }

  it('reports the credential in the fields the relying party reads, all base64url', async () => {
    stubAuthenticator(() =>
      Promise.resolve({
        id: 'Y3JlZC1pZA',
        rawId: buffer(1, 2, 3, 4),
        type: 'public-key',
        response: {
          attestationObject: buffer(9, 8, 7),
          clientDataJSON: buffer(5, 6),
          getTransports: () => ['internal', 'cable'],
        },
        getClientExtensionResults: () => ({}),
      } as unknown as Credential),
    )

    const credential = (await createPasskey(CREATION_OPTIONS)) as {
      id: string
      rawId: string
      type: string
      response: { attestationObject: string; clientDataJSON: string; transports?: string[] }
    }

    expect(credential.id).toBe('Y3JlZC1pZA')
    expect(credential.rawId).toBe(bytesToBase64Url(buffer(1, 2, 3, 4)))
    expect(credential.type).toBe('public-key')
    expect(credential.response.clientDataJSON).toBe(bytesToBase64Url(buffer(5, 6)))
    // `cable` is dropped here too: the transports the browser reports go straight to the same
    // closed enumeration on the server.
    expect(credential.response.transports).toEqual(['internal'])
  })

  it('reports a dismissed platform dialog as a cancellation, not as a failure', async () => {
    stubAuthenticator(() => Promise.reject(new DOMException('refused', 'NotAllowedError')))
    await expect(createPasskey(CREATION_OPTIONS)).rejects.toBeInstanceOf(PasskeyCancelled)
  })

  it('reports a null credential as a cancellation too', async () => {
    stubAuthenticator(() => Promise.resolve(null))
    await expect(createPasskey(CREATION_OPTIONS)).rejects.toBeInstanceOf(PasskeyCancelled)
  })

  it('passes a real failure through rather than hiding it as a cancellation', async () => {
    const real = new DOMException('the relying party is wrong', 'SecurityError')
    stubAuthenticator(() => Promise.reject(real))
    await expect(createPasskey(CREATION_OPTIONS)).rejects.toBe(real)
  })
})

describe('a browser that cannot do this at all', () => {
  it('is detected before the control is offered', () => {
    expect(isPasskeySupported()).toBe(false)
  })

  it('refuses rather than throwing something a screen cannot describe', async () => {
    await expect(createPasskey(CREATION_OPTIONS)).rejects.toBeInstanceOf(PasskeyUnsupported)
  })
})
