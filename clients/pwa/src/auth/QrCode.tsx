import { useMemo } from 'react'
import qrcode from 'qrcode-generator'
import './auth.css'

/**
 * A QR code, drawn as inline SVG.
 *
 * ## Why a library, and this one
 *
 * Encoding a QR code correctly means Reed–Solomon error correction over GF(256), eight mask patterns
 * scored against four penalty rules, and the version tables. Getting any of it subtly wrong produces
 * a code that *looks* like a QR code and does not scan, which is the worst possible failure here:
 * the person tries three times, blames their phone, and types the key in by hand while a customer
 * waits. `qrcode-generator` is the reference implementation of the format, MIT-licensed, with no
 * dependencies of its own — which is what keeps a sign-in path free of a transitive supply chain.
 *
 * ## Why inline SVG rather than a canvas or a data URI
 *
 * The Content Security Policy allows `img-src data:`, so a data URI would work — but an SVG drawn
 * into the document scales to any zoom level without blurring, prints at the printer's resolution
 * rather than the screen's, and needs no `<canvas>` that a screen reader has to be told to ignore.
 * The whole code is one `<path>`: a thousand `<rect>` elements is a thousand DOM nodes for something
 * that is a single shape.
 *
 * ## Colour
 *
 * Deliberately not themed. `--colour-code-surface` and `--colour-code-ink` are white and black in
 * every theme, because a camera decoding this needs the contrast, and an inverted code is one most
 * authenticator apps refuse outright.
 */
export interface QrCodeProps {
  /** What the code encodes. For an enrolment this is a live secret; it is drawn and never stored. */
  readonly value: string
  /**
   * What the code is, in words, for anybody who cannot see it.
   *
   * It says what scanning would achieve — never the encoded value. Reading a shared secret aloud to
   * a room is not an accessible alternative to a picture of it; the alternative is the setup key
   * beside it, which is a field a screen reader can spell out on request.
   */
  readonly label: string
  readonly className?: string
}

/** The quiet zone the specification requires around a code, in modules. */
const QUIET_ZONE = 4

export function QrCode({ value, label, className }: QrCodeProps) {
  const path = useMemo(() => {
    try {
      // Type 0 chooses the smallest version that fits. Level M corrects about 15% of the code,
      // which is what survives a slightly bent screen protector and a shop-floor camera.
      const code = qrcode(0, 'M')
      code.addData(value)
      code.make()

      const modules = code.getModuleCount()
      const segments: string[] = []
      for (let row = 0; row < modules; row += 1) {
        for (let column = 0; column < modules; column += 1) {
          if (code.isDark(row, column)) {
            segments.push(`M${String(column)} ${String(row)}h1v1h-1z`)
          }
        }
      }
      return { d: segments.join(''), modules }
    } catch {
      // Nothing to draw. The screen always shows the setup key and the link as well, so the person
      // is never stuck — which is exactly why this failure is silent rather than an error state.
      return null
    }
  }, [value])

  if (path === null) {
    return null
  }

  const extent = path.modules + QUIET_ZONE * 2

  return (
    <svg
      aria-label={label}
      className={className === undefined ? 'qr-code' : `qr-code ${className}`}
      role="img"
      viewBox={`${String(-QUIET_ZONE)} ${String(-QUIET_ZONE)} ${String(extent)} ${String(extent)}`}
      xmlns="http://www.w3.org/2000/svg"
    >
      <rect
        className="qr-code__ground"
        height={extent}
        width={extent}
        x={-QUIET_ZONE}
        y={-QUIET_ZONE}
      />
      <path className="qr-code__modules" d={path.d} />
    </svg>
  )
}
