import { useIntl } from 'react-intl'
import { Button } from '../../components/primitives/Button'
import type { GarmentDesignSnapshot } from '../../catalog/types'
import { IllustrationPreview } from './IllustrationPreview'
import './garmentDesignCard.css'

export interface GarmentDesignCardProps {
  readonly snapshot: GarmentDesignSnapshot
  /** Shows the print control. Defaults to true; a caller embedding this read-only elsewhere turns it off. */
  readonly printable?: boolean
  readonly className?: string
}

/**
 * The design a confirmed garment carries, on screen and on paper (#142).
 *
 * Renders exactly what `GarmentDesignSnapshot` holds and nothing it has to ask the catalogue for —
 * the whole point of a snapshot (`docs/prd/design-options.md` section 7) is that it survives a
 * retired option or a renamed group, so this component makes no catalogue request of its own; a test
 * that stubs no catalogue route and still sees a two-year-old snapshot render is what proves it.
 *
 * Printed at A4 through the same `body:has(...)` isolation `measurements.css` uses, and monochrome
 * on purpose: nothing here relies on colour alone. An illustrated option prints its bundled key as
 * the same accessible placeholder it shows on screen, unless #31 has by then given it a real image;
 * an option with no drawing at all prints its label and alt text, which is what
 * `IllustrationPreview` already renders for the picker.
 */
export function GarmentDesignCard({
  snapshot,
  printable = true,
  className,
}: GarmentDesignCardProps) {
  const intl = useIntl()
  const selections = [...snapshot.selections].sort(
    (left, right) => left.groupDisplayOrder - right.groupDisplayOrder,
  )

  return (
    <div className={['job-card', className].filter(Boolean).join(' ')}>
      <div className="job-card__body">
        <header className="job-card__header">
          <h2>{snapshot.serviceTypeLabel}</h2>
          <p className="job-card__meta">
            {intl.formatMessage(
              { id: 'catalog.design.card.meta' },
              { category: snapshot.categoryLabel, version: String(snapshot.catalogVersionNumber) },
            )}
          </p>
        </header>

        <dl className="job-card__selections">
          {selections.map((selection) => (
            <div className="job-card__selection" key={selection.groupCode}>
              <dt>{selection.groupLabel}</dt>
              <dd>
                <IllustrationPreview
                  alt={selection.illustrationAlt}
                  illustrationKey={selection.illustrationKey}
                />
                <span className="job-card__optionLabel">{selection.optionLabel}</span>
              </dd>
            </div>
          ))}
        </dl>

        {snapshot.conditionalNotes.length === 0 ? null : (
          <section aria-labelledby="job-card-notes" className="job-card__notes">
            <h3 id="job-card-notes">{intl.formatMessage({ id: 'catalog.design.card.notes' })}</h3>
            <ul>
              {snapshot.conditionalNotes.map((note) => (
                <li key={note}>{note}</li>
              ))}
            </ul>
          </section>
        )}

        {snapshot.instructions === null || snapshot.instructions.trim() === '' ? null : (
          <p className="job-card__instructions">
            <strong>{intl.formatMessage({ id: 'catalog.design.card.instructions' })}</strong>{' '}
            {snapshot.instructions}
          </p>
        )}

        {printable ? (
          <p className="job-card__actions">
            <Button
              iconName="clipboard"
              onClick={() => {
                window.print()
              }}
              variant="primary"
            >
              {intl.formatMessage({ id: 'catalog.design.card.print' })}
            </Button>
          </p>
        ) : null}
      </div>
    </div>
  )
}
