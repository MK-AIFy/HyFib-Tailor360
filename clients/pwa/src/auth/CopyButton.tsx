import { useState } from 'react'
import { Button } from '../components/primitives/Button'
import './auth.css'

/**
 * Copies a value to the clipboard, and says whether it worked.
 *
 * It exists because of 3.3.8 Accessible Authentication: a setup key and a recovery code must be
 * transferable without transcription, and "select the text and press control-C" is not a mechanism
 * on a counter tablet. The confirmation is a polite live region rather than a toast, because a
 * message that disappears is not a way to tell somebody whether their recovery codes were saved.
 *
 * The failure path matters more than the success path. `navigator.clipboard` is absent over plain
 * HTTP, absent in some embedded webviews, and refused outright when the document is not focused —
 * all three of which happen on real shop devices. A silent failure would leave somebody believing
 * they had copied their recovery codes, so the failure is said out loud and points at the visible
 * text they can still read.
 */
export interface CopyButtonProps {
  /** What to copy. Never logged, and never held anywhere but this call. */
  readonly value: string
  /** The control's visible name. Says what is being copied — 2.5.3 Label in Name. */
  readonly label: string
  /** What to announce when it worked. */
  readonly confirmation: string
  /** What to announce when the browser refused. */
  readonly failure: string
}

type Outcome = 'idle' | 'copied' | 'failed'

export function CopyButton({ value, label, confirmation, failure }: CopyButtonProps) {
  const [outcome, setOutcome] = useState<Outcome>('idle')

  const copy = () => {
    // Declared non-optional by the DOM types and absent in practice over plain HTTP and in some
    // embedded webviews, which is exactly where a shop device is most likely to be.
    const clipboard: Clipboard | undefined = navigator.clipboard
    if (clipboard === undefined || typeof clipboard.writeText !== 'function') {
      setOutcome('failed')
      return
    }

    clipboard.writeText(value).then(
      () => {
        setOutcome('copied')
      },
      () => {
        setOutcome('failed')
      },
    )
  }

  return (
    <span className="copy-control">
      <Button iconName="clipboard" onClick={copy} variant="secondary">
        {label}
      </Button>
      {/* Present from first render rather than appearing with its text: a region inserted at the
          same moment as its content is the commonest reason an announcement is never made. */}
      <span className="copy-control__outcome" role="status">
        {outcome === 'copied' ? confirmation : outcome === 'failed' ? failure : ''}
      </span>
    </span>
  )
}
