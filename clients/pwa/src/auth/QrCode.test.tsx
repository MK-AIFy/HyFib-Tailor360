import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { QrCode } from './QrCode'

const OTPAUTH =
  'otpauth://totp/HyFib%20Tailor360:asha.counter?secret=MFZWIZTBONSGC3THMFZWIZTB&issuer=HyFib%20Tailor360&digits=6&period=30'

function pathOf(container: HTMLElement): string {
  return container.querySelector('path')?.getAttribute('d') ?? ''
}

describe('the enrolment QR code', () => {
  it('is an image with a name that says what it is for', () => {
    render(
      <QrCode label="A QR code that adds asha.counter to an authenticator app." value={OTPAUTH} />,
    )

    expect(screen.getByRole('img')).toHaveAccessibleName(
      'A QR code that adds asha.counter to an authenticator app.',
    )
  })

  it('draws the whole code as one path rather than a thousand nodes', () => {
    const { container } = render(<QrCode label="A code" value={OTPAUTH} />)

    expect(container.querySelectorAll('path')).toHaveLength(1)
    expect(pathOf(container).length).toBeGreaterThan(100)
  })

  it('encodes what it was given, so two different secrets are two different codes', () => {
    const first = render(<QrCode label="A code" value={OTPAUTH} />)
    const second = render(<QrCode label="A code" value={`${OTPAUTH}&x=1`} />)

    expect(pathOf(first.container)).not.toBe(pathOf(second.container))
  })

  it('keeps the quiet zone the specification requires around it', () => {
    const { container } = render(<QrCode label="A code" value={OTPAUTH} />)

    // Without four modules of clear space a decoder cannot find the finder patterns, and the code
    // reads as "sometimes works, depending on the background".
    const viewBox = container.querySelector('svg')?.getAttribute('viewBox') ?? ''
    const [x, y] = viewBox.split(' ')
    expect(x).toBe('-4')
    expect(y).toBe('-4')
  })

  it('paints on a surface that does not follow the theme', () => {
    const { container } = render(<QrCode label="A code" value={OTPAUTH} />)

    // A camera decoding this needs dark modules on a light ground. Inverting them for the dark theme
    // produces a code most authenticator apps refuse outright, so the ground and the ink are their
    // own tokens rather than the page's.
    expect(container.querySelector('.qr-code__ground')).toBeInTheDocument()
    expect(container.querySelector('.qr-code__modules')).toBeInTheDocument()
  })

  it('renders nothing rather than a broken picture when the value cannot be encoded', () => {
    // The setup key and the link are always on the screen as well, so there is nothing to recover
    // from here — which is why this failure is silent rather than an error state.
    const { container } = render(<QrCode label="A code" value={'x'.repeat(10_000)} />)

    expect(container.querySelector('svg')).toBeNull()
  })
})
