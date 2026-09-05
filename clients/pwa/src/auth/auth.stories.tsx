import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { Button } from '../components/primitives/Button'
import { FactorFields } from './FactorFields'
import { QrCode } from './QrCode'
import { RecoveryCodes } from './RecoveryCodes'
import { SessionExpiryDialog } from './SessionExpiryDialog'
import type { ChallengeFactor, SessionExpiry } from './types'
import './auth.css'

/**
 * The parts of the authentication journey that can be looked at without a server.
 *
 * The screens themselves are driven entirely by what the API answers — a sign-in has five failures
 * that must be indistinguishable, an enrolment carries a live secret, a challenge depends on which
 * factors an account holds — so they are reviewed in the tests, which can state what the server
 * said. What is worth looking at here is the handful of pieces whose whole job is how they read: the
 * QR code and the sheet of recovery codes somebody has one chance to keep, the second-factor
 * question, and the two-minute warning.
 */
const meta = {
  title: 'Authentication/Pieces',
  parameters: { layout: 'centered' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

const OTPAUTH =
  'otpauth://totp/HyFib%20Tailor360:asha.counter?secret=MFZWIZTBONSGC3THMFZWIZTB' +
  '&issuer=HyFib%20Tailor360&digits=6&period=30'

/** Invented codes, in the printed shape. Nothing here is a working credential for anything. */
const CODES = [
  '4RJ2-8QKD',
  '9WTC-2MBE',
  'H7XA-51PN',
  'K3DV-QY68',
  'P2LM-73RF',
  'T8NB-46WS',
  'V5GH-90CJ',
  'Z1QY-28DK',
]

/**
 * The QR code an authenticator app is pointed at.
 *
 * Its colours deliberately do not follow the theme. Switch the toolbar to the dark or the
 * high-contrast theme and this stays black on white, because a camera decoding it needs that
 * contrast and most authenticator apps refuse an inverted code outright.
 */
export const EnrolmentQrCode: Story = {
  render: () => (
    <div className="qr-frame">
      <QrCode
        label="A QR code that adds asha.counter at HyFib Tailor360 to an authenticator app."
        value={OTPAUTH}
      />
    </div>
  ),
}

/**
 * The one and only moment the recovery codes exist in a readable form.
 *
 * Three things are load-bearing and all three are visible here: the warning comes before the codes,
 * there are two ways to keep them, and "Finish" is gated on an acknowledgement rather than being the
 * button somebody presses to make the screen go away.
 */
export const RecoveryCodeSheet: Story = {
  render: () => <RecoveryCodes codes={CODES} onDone={() => undefined} />,
}

/** The second-factor question, with both factors available so the choice is offered. */
export const SecondFactorWithAChoice: Story = {
  render: function Render() {
    const [factor, setFactor] = useState<ChallengeFactor>('totp')
    const [code, setCode] = useState('')

    return (
      <div className="auth-panel">
        <FactorFields
          code={code}
          factor={factor}
          factors={{ authenticator: true, recoveryCode: true, passkey: false }}
          onCodeChange={setCode}
          onFactorChange={setFactor}
        />
      </div>
    )
  },
}

/** The same question for an account that has only an authenticator: no choice, no radio group. */
export const SecondFactorWithoutAChoice: Story = {
  render: function Render() {
    const [code, setCode] = useState('')

    return (
      <div className="auth-panel">
        <FactorFields
          code={code}
          factor="totp"
          factors={{ authenticator: true, recoveryCode: false, passkey: false }}
          onCodeChange={setCode}
          onFactorChange={() => undefined}
        />
      </div>
    )
  },
}

/**
 * The two-minute warning (WCAG 2.2.1).
 *
 * Focus lands on "Carry on working", which is one press and makes one ordinary request. The visible
 * countdown ticks every second and is hidden from assistive technology; the announcement beside it
 * changes only at two minutes, one minute, thirty seconds and ten.
 */
export const SessionAboutToEnd: Story = {
  render: function Render() {
    const [open, setOpen] = useState(false)
    const expiry: SessionExpiry = {
      idleExpiresAt: new Date(Date.now() + 120_000).toISOString(),
      absoluteExpiresAt: new Date(Date.now() + 6 * 60 * 60 * 1000).toISOString(),
      warningLeadSeconds: 120,
      mfaSatisfied: true,
    }

    return (
      <>
        <Button
          onClick={() => {
            setOpen(true)
          }}
          variant="primary"
        >
          Show the warning
        </Button>
        {open ? (
          <SessionExpiryDialog
            expiry={expiry}
            onExpired={() => {
              setOpen(false)
            }}
            onKeepWorking={() => {
              setOpen(false)
            }}
            onSignOut={() => {
              setOpen(false)
            }}
          />
        ) : null}
      </>
    )
  },
}
