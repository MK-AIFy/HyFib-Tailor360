import { useId } from 'react'
import type { ReactNode } from 'react'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import './DataTable.css'

export interface DataTableColumn<TRow> {
  /** Stable identifier, used as the React key and as the cell's `data-column`. */
  readonly id: string
  /** The column heading, already translated. */
  readonly header: string
  /** Renders the cell. */
  readonly cell: (row: TRow) => ReactNode
  /**
   * The column that identifies the row — job number, customer, SKU. Exactly one column should carry
   * it: in the card layout it becomes the card's leading line, so a card that is scrolled past at
   * arm's length still says which record it is.
   */
  readonly primary?: boolean
  /** Numbers, amounts and quantities: aligned to the end and set in the tabular figures. */
  readonly numeric?: boolean
  /** Hides the column in the card layout, for detail that only earns its place in a wide table. */
  readonly hideWhenNarrow?: boolean
}

export interface DataTableProps<TRow> {
  /**
   * What the table is a table of. Rendered as a `<caption>`, which is what names it for a screen
   * reader's table list and for the scroll region around it. Hide it visually with `hideCaption` if
   * the surrounding heading already says it — but never omit it.
   */
  readonly caption: string
  readonly hideCaption?: boolean
  readonly columns: readonly DataTableColumn<TRow>[]
  readonly rows: readonly TRow[]
  readonly rowKey: (row: TRow) => string
  /**
   * A short name for the row — "job J-CBE01-2627-000512-01", "Lakshmi Narayanan".
   *
   * Required, because checklist item A11Y-60 is: a row action must announce **which** row it belongs
   * to. Eleven identical "Print" buttons in a delivery queue is a custody error waiting to happen.
   * The name is used for the row's action group and is what a caller should fold into each action's
   * own accessible name.
   */
  readonly rowLabel: (row: TRow) => string
  /** Controls for a single row. Each one must name the row; `rowLabel` is what to name it with. */
  readonly rowActions?: (row: TRow) => ReactNode
  /** Shown instead of the rows when there are none. Defaults to a plain sentence. */
  readonly empty?: ReactNode
  readonly className?: string
}

/**
 * A table that becomes a list of cards when it runs out of room.
 *
 * ## Why it is one DOM tree and not two
 *
 * The obvious implementation renders a `<table>` at wide widths and a list of cards at narrow ones,
 * switching on a measured width. It is the wrong one: it needs JavaScript to decide layout, it
 * duplicates every cell, and it re-mounts the whole collection at the breakpoint, which throws away
 * focus mid-task. This renders one table and restyles it with a **container query**, so the switch
 * costs nothing, happens at the width of the *container* rather than of the device — a table in a
 * narrow master-detail pane becomes cards on a desktop, which is correct — and needs no measurement.
 *
 * ## Why every ARIA role is written out
 *
 * Changing `display` on a table element strips the implicit table semantics in every browser: a
 * `<tr>` set to `display: block` is no longer a row, and its cells lose the association with their
 * column headers. The explicit `role="table"`, `role="rowgroup"`, `role="row"`, `role="columnheader"`
 * and `role="cell"` below put them back, unconditionally, in both layouts. They are not redundant
 * decoration — remove them and the card layout announces fifteen unlabelled fragments.
 *
 * ## Reflow
 *
 * 1.4.10 says the *page* must not scroll horizontally at 320 CSS px; a wide table scrolls inside its
 * own container instead. That container is focusable and named, so a keyboard user can reach and
 * scroll it — an overflow container that cannot be focused is unreachable without a mouse.
 */
export function DataTable<TRow>({
  caption,
  hideCaption = false,
  columns,
  rows,
  rowKey,
  rowLabel,
  rowActions,
  empty,
  className,
}: DataTableProps<TRow>) {
  const intl = useIntl()
  const captionId = useId()
  const columnCount = columns.length + (rowActions === undefined ? 0 : 1)

  return (
    <div className={cx('data-table', className)}>
      <div
        className="data-table__scroll"
        role="region"
        aria-labelledby={captionId}
        // A scrollable region has to be reachable from the keyboard; without this the only way to
        // see the far columns is a pointer, which the counter desktop does not always have.
        tabIndex={0}
      >
        <table className="data-table__table" role="table">
          <caption
            className={cx('data-table__caption', hideCaption && 'visually-hidden')}
            id={captionId}
          >
            {caption}
          </caption>
          <thead className="data-table__head" role="rowgroup">
            <tr className="data-table__row" role="row">
              {columns.map((column) => (
                <th
                  key={column.id}
                  className="data-table__header-cell"
                  role="columnheader"
                  scope="col"
                  data-numeric={column.numeric === true ? 'true' : undefined}
                >
                  {column.header}
                </th>
              ))}
              {rowActions === undefined ? null : (
                <th className="data-table__header-cell" role="columnheader" scope="col">
                  <span className="visually-hidden">
                    {intl.formatMessage({ id: 'primitives.table.rowActions' }, { row: caption })}
                  </span>
                </th>
              )}
            </tr>
          </thead>
          <tbody className="data-table__body" role="rowgroup">
            {rows.length === 0 ? (
              <tr className="data-table__row" role="row">
                <td className="data-table__empty" role="cell" colSpan={columnCount}>
                  {empty ?? intl.formatMessage({ id: 'primitives.table.empty' })}
                </td>
              </tr>
            ) : (
              rows.map((row) => (
                <tr key={rowKey(row)} className="data-table__row" role="row">
                  {columns.map((column) => (
                    <td
                      key={column.id}
                      className="data-table__cell"
                      role="cell"
                      data-column={column.id}
                      data-primary={column.primary === true ? 'true' : undefined}
                      data-numeric={column.numeric === true ? 'true' : undefined}
                      data-hide-when-narrow={column.hideWhenNarrow === true ? 'true' : undefined}
                    >
                      {/* The column name repeated inside the cell, shown only in the card layout
                          where the header row is off screen. aria-hidden because the cell is still
                          associated with its columnheader by role, and a screen reader that read
                          both would say every label twice. */}
                      <span className="data-table__cell-label" aria-hidden="true">
                        {column.header}
                      </span>
                      <span className="data-table__cell-value">{column.cell(row)}</span>
                    </td>
                  ))}
                  {rowActions === undefined ? null : (
                    <td className="data-table__cell data-table__cell--actions" role="cell">
                      <div
                        className="data-table__row-actions"
                        role="group"
                        aria-label={intl.formatMessage(
                          { id: 'primitives.table.rowActions' },
                          { row: rowLabel(row) },
                        )}
                      >
                        {rowActions(row)}
                      </div>
                    </td>
                  )}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
