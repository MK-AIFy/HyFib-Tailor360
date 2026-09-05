import { useId } from 'react'
import type { ReactNode } from 'react'
import { cx } from '../../design-system/foundations/cx'
import './Card.css'

/** Heading levels a card may take. There is no `h1`: a card is never the page. */
export type CardHeadingLevel = 2 | 3 | 4 | 5 | 6

export interface CardProps {
  readonly children: ReactNode
  /** The card's heading. A card in a list of cards should have one, so the list is navigable by heading. */
  readonly title?: ReactNode
  /**
   * Which heading level the title renders as.
   *
   * Required whenever a title is given, and deliberately not defaulted to `h3`: 1.3.1 and checklist
   * item A11Y-25 are about the document outline being true, and a component that guesses a level
   * produces the skipped headings that make a screen reader's heading list useless. The caller knows
   * what it nested this card inside; the card does not.
   */
  readonly headingLevel?: CardHeadingLevel
  /** A status badge, a due cue — anything that belongs on the title row rather than in the body. */
  readonly meta?: ReactNode
  /** The row of controls at the foot of the card. Use `ButtonGroup` so the spacing rule is applied. */
  readonly actions?: ReactNode
  /** Raises the card off the page. Shadow is never the only signal: a border is drawn either way. */
  readonly raised?: boolean
  /** Marks the card as the currently selected item in a master-detail layout. */
  readonly selected?: boolean
  readonly className?: string
}

/**
 * A bounded block of related content.
 *
 * It is a container, not a control, and that is the important decision. A card that is itself a
 * button cannot hold a link, a status badge and three row actions without nesting interactive
 * elements inside an interactive element — which no screen reader announces sensibly and no keyboard
 * user can reach past. So the whole card is never clickable: the title carries the link, and the
 * actions carry the rest.
 *
 * With a title it renders as an `article` named by that title, which puts it in the screen reader's
 * landmark and heading lists as one findable thing; without a title it is a plain `div`, because an
 * unnamed `article` adds a level to the tree and says nothing.
 */
export function Card({
  children,
  title,
  headingLevel = 3,
  meta,
  actions,
  raised = false,
  selected = false,
  className,
}: CardProps) {
  const titleId = useId()
  const Heading = `h${String(headingLevel)}` as 'h2'

  const content = (
    <>
      {title === undefined && meta === undefined ? null : (
        <div className="card__header">
          {title === undefined ? null : (
            <Heading className="card__title" id={titleId}>
              {title}
            </Heading>
          )}
          {meta === undefined ? null : <div className="card__meta">{meta}</div>}
        </div>
      )}
      <div className="card__body">{children}</div>
      {actions === undefined ? null : <div className="card__actions">{actions}</div>}
    </>
  )

  if (title === undefined) {
    return (
      <div
        className={cx('card', className)}
        data-raised={raised ? 'true' : undefined}
        data-selected={selected ? 'true' : undefined}
      >
        {content}
      </div>
    )
  }

  return (
    <article
      className={cx('card', className)}
      aria-labelledby={titleId}
      data-raised={raised ? 'true' : undefined}
      data-selected={selected ? 'true' : undefined}
    >
      {content}
    </article>
  )
}
