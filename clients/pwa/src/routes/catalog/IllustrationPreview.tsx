import { FormattedMessage } from 'react-intl'
import { Alert } from '../../components/primitives/Alert'

/**
 * What a design option's illustration looks like today: a key, or a warning that there is none
 * (#141).
 *
 * ## Why this is a placeholder rather than an image
 *
 * `docs/prd/design-options.md` names sixteen bundled illustration sheets by key, but no Media
 * endpoint streams one yet and no bundled asset ships with this client (#31 is the epic that adds
 * both). Until then this shows what the picker itself falls back to — the key as a fact, and the
 * alternative text a customer would actually be told — rather than an `<img>` pointed at nothing.
 * This repository's security rules say media is never given a URL, so a live preview will stream
 * through an authorising endpoint when #31 lands, not through this component growing a `src`.
 */
export interface IllustrationPreviewProps {
  readonly illustrationKey: string | null
  readonly alt: string
}

export function IllustrationPreview({ illustrationKey, alt }: IllustrationPreviewProps) {
  if (illustrationKey === null) {
    return (
      <Alert live="off" tone="warning">
        <FormattedMessage id="catalog.design.option.illustration.missing" />
        {alt.trim() === '' ? null : <p>{alt}</p>}
      </Alert>
    )
  }

  return (
    <figure>
      <div aria-label={alt} role="img">
        <FormattedMessage
          id="catalog.design.option.illustration.key"
          values={{ key: illustrationKey }}
        />
      </div>
      <figcaption>{alt}</figcaption>
    </figure>
  )
}
