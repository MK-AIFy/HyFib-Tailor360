import { useRef, useState } from 'react'
import { useShellStatus } from '../../components/layout/useShellStatus'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ButtonGroup } from '../../components/primitives/ButtonGroup'
import { Card } from '../../components/primitives/Card'
import { DataTable } from '../../components/primitives/DataTable'
import { Icon } from '../../components/primitives/Icon'
import { useDemoText } from '../../components/primitives/demoText'
import { Forbidden } from '../../components/states/Forbidden'
import { getFormatters } from '../../i18n/formatters'
import { BRANCH, NOW, STAFF } from '../fixtures/branch'
import { ALERTS, PIPELINE, SALES_REPORT, TILES } from '../fixtures/dashboard'
import { STOCK_ITEMS } from '../fixtures/inventory'

/** The tallest bar in the pipeline, so the drawing has a scale. */
const PIPELINE_PEAK = Math.max(...PIPELINE.map((row) => row.jobs))

/**
 * `A11Y-RJ-08` — Owner: the dashboard, its alerts and a report.
 *
 * Its own record in docs/nfr/a11y-checklist.md section 6.8 — reports and alerts, which no other
 * record opens. All eight steps are reachable here.
 *
 * ## The chart is drawn from the table, not the other way round
 *
 * The bars are an `aria-hidden` SVG whose rectangles are sized by attributes, not by an inline style
 * — the content security policy forbids inline styles, and a chart is not a good enough reason to
 * ask for an exception. The table underneath is not a fallback tucked behind a disclosure: it is the
 * data, it is always rendered, its scroll region is focusable, and the drawing is decoration over
 * it. That ordering is the only one that cannot end in a canvas nobody can read.
 *
 * ## Focus comes back to where it left
 *
 * Taking an alert's action opens a panel; leaving the panel puts focus back on the control that
 * opened it, which is step 5 and checklist item A11Y-35. A panel that dropped focus to the document
 * body would send a keyboard user back to the top of a dashboard every time they read an alert.
 *
 * ## A figure this role may not read is a state, not an error
 *
 * The staff-cost tile is refused, and the refusal says which permission it needs and who to ask.
 * "Something went wrong" would send somebody to look for a fault that does not exist.
 *
 * ## The dashboard holds still
 *
 * Step 8, and 2.2.2 Pause, Stop, Hide: nothing re-orders under the reader's hands. Updates that have
 * arrived are counted and offered behind a control, and they are applied when somebody asks for
 * them. A dashboard that refreshed itself would move the row somebody was in the middle of reading.
 *
 * There is no backend: the state is `useState` over the fixtures.
 */
export function OwnerDashboardScreen() {
  const t = useDemoText()
  const formatters = getFormatters()
  const status = useShellStatus()

  const [openAlertId, setOpenAlertId] = useState<string | null>(null)
  const [forbiddenTileId, setForbiddenTileId] = useState<string | null>(null)
  const [pendingUpdates, setPendingUpdates] = useState(2)

  /** The control each panel was opened from, so focus can be put back on it. */
  const triggers = useRef(new Map<string, HTMLButtonElement | null>())

  function returnFocusTo(id: string) {
    triggers.current.get(id)?.focus()
  }

  const openAlert = ALERTS.find((alert) => alert.id === openAlertId)
  const forbiddenTile = TILES.find((tile) => tile.id === forbiddenTileId)
  const lowStockItem = STOCK_ITEMS.find((item) => item.id === 'ST-0107')

  return (
    <section className="page journey-screen">
      <h1>{t('Dashboard')}</h1>
      <p>
        {t(STAFF.manager)} · {t(BRANCH.name)} · {formatters.formatDateTime(NOW)}
      </p>

      {/* Step 1: the count of open alerts, in the document flow, before the tiles. */}
      <p role="status">
        {t(
          `${String(ALERTS.length)} alerts need attention. Financial year ${BRANCH.financialYear}.`,
        )}
      </p>

      {/* Step 8: updates are counted and offered, never applied under the reader's hands. */}
      {pendingUpdates === 0 ? null : (
        <Alert
          actions={
            <Button
              iconName="refresh"
              onClick={() => {
                setPendingUpdates(0)
                status.announceAutosave(t('Dashboard updated.'))
              }}
            >
              {t(`Show ${String(pendingUpdates)} new updates`)}
            </Button>
          }
          live="polite"
          tone="info"
        >
          {t(
            `${String(pendingUpdates)} updates have arrived since this screen opened. Nothing on the screen has moved: they are applied when you ask for them.`,
          )}
        </Alert>
      )}

      <section aria-labelledby="dash-tiles" className="journey-section">
        <h2 id="dash-tiles">{t('2. This branch at a glance')}</h2>
        <ul className="journey-tiles">
          {TILES.map((tile) => (
            <li key={tile.id}>
              <Card headingLevel={3} title={t(tile.label)}>
                {tile.forbidden === true ? (
                  <>
                    <p className="journey-tile__period">{t(tile.period)}</p>
                    <ButtonGroup>
                      <Button
                        onClick={() => {
                          setForbiddenTileId(tile.id)
                        }}
                        ref={(element) => {
                          triggers.current.set(tile.id, element)
                        }}
                      >
                        {t(`Open ${tile.label}`)}
                      </Button>
                    </ButtonGroup>
                  </>
                ) : (
                  <>
                    <p className="journey-tile__value journey-amount">
                      {tile.id === 'sales'
                        ? formatters.formatMoney(tile.value)
                        : formatters.formatNumber(tile.value)}
                    </p>
                    {/* Step 2: the period travels with the figure. A number with no period is unusable. */}
                    <p className="journey-tile__period">{t(tile.period)}</p>
                  </>
                )}
              </Card>
            </li>
          ))}
        </ul>
      </section>

      {forbiddenTile === undefined ? null : (
        <Forbidden
          action={t(`reading ${forbiddenTile.label}`)}
          allowedRoles={[t('Owner'), t('Accountant')]}
          headingLevel={2}
          onBack={() => {
            setForbiddenTileId(null)
            returnFocusTo(forbiddenTile.id)
          }}
        >
          {t(
            'Staff cost is a payroll figure and needs the payroll read permission, which this account does not hold. Ask the owner to grant it, or ask them for the figure.',
          )}
        </Forbidden>
      )}

      <section aria-labelledby="dash-pipeline" className="journey-section">
        <h2 id="dash-pipeline">{t('3. Where the work is')}</h2>

        {/*
          Decoration over the table below. `aria-hidden` so it is never announced, and every
          dimension is an SVG attribute rather than a style, because the content security policy
          forbids an inline style and a chart does not earn an exception to it.
        */}
        <svg
          aria-hidden="true"
          className="journey-chart"
          role="presentation"
          viewBox={`0 0 ${String(PIPELINE.length * 40)} 100`}
        >
          {PIPELINE.map((row, index) => {
            const height = Math.round((row.jobs / PIPELINE_PEAK) * 90)
            return (
              <rect
                height={height}
                key={row.phase}
                width={24}
                x={index * 40 + 8}
                y={100 - height}
              />
            )
          })}
        </svg>

        {/* The data itself, always rendered, in a focusable region. Not a fallback. */}
        <DataTable
          caption={t('Jobs in each phase, and how many of them are overdue')}
          columns={[
            { id: 'phase', header: t('Phase'), primary: true, cell: (row) => t(row.phase) },
            {
              id: 'jobs',
              header: t('Jobs'),
              numeric: true,
              cell: (row) => formatters.formatNumber(row.jobs),
            },
            {
              id: 'overdue',
              header: t('Overdue'),
              numeric: true,
              cell: (row) => formatters.formatNumber(row.overdue),
            },
          ]}
          rowKey={(row) => row.phase}
          rowLabel={(row) => t(row.phase)}
          rows={PIPELINE}
        />
      </section>

      <section aria-labelledby="dash-alerts" className="journey-section">
        <h2 id="dash-alerts">{t('4. Alerts')}</h2>
        <ul className="journey-list">
          {ALERTS.map((alert) => (
            <li key={alert.id}>
              <Card
                actions={
                  <ButtonGroup>
                    <Button
                      onClick={() => {
                        setOpenAlertId(alert.id)
                      }}
                      ref={(element) => {
                        triggers.current.set(alert.id, element)
                      }}
                      variant="primary"
                    >
                      {t(alert.actionLabel)}
                      {/* Which alert, for somebody who arrived at the button rather than the card. */}{' '}
                      <span className="visually-hidden">— {t(alert.title)}</span>
                    </Button>
                  </ButtonGroup>
                }
                headingLevel={3}
                meta={<Icon name="alert-circle" />}
                title={t(alert.title)}
              >
                {/* Item, location and shortfall in words and units — never "below reorder level". */}
                <p>{t(alert.detail)}</p>
              </Card>
            </li>
          ))}
        </ul>
      </section>

      {openAlert === undefined ? null : (
        <section aria-labelledby="dash-alert-detail" className="journey-section">
          <h2 id="dash-alert-detail">{t(openAlert.title)}</h2>
          <Card
            headingLevel={3}
            title={t(openAlert.id === 'low-stock-hooks' ? 'The stock item' : 'The held job')}
          >
            <dl className="journey-summary">
              {openAlert.id === 'low-stock-hooks' && lowStockItem !== undefined ? (
                <>
                  <dt>{t('Item')}</dt>
                  <dd>{t(lowStockItem.name)}</dd>
                  <dt>{t('Location')}</dt>
                  <dd>{t(lowStockItem.location)}</dd>
                  <dt>{t('On hand')}</dt>
                  <dd className="journey-amount">
                    {formatters.formatQuantity(lowStockItem.onHand, lowStockItem.unitSymbol)}
                  </dd>
                  <dt>{t('Reorder level')}</dt>
                  <dd className="journey-amount">
                    {formatters.formatQuantity(lowStockItem.reorderLevel, lowStockItem.unitSymbol)}
                  </dd>
                  <dt>{t('Shortfall')}</dt>
                  <dd className="journey-amount">
                    {formatters.formatQuantity(
                      lowStockItem.reorderLevel - lowStockItem.onHand,
                      lowStockItem.unitSymbol,
                    )}
                  </dd>
                </>
              ) : (
                <>
                  <dt>{t('What is held')}</dt>
                  <dd>{t(openAlert.detail)}</dd>
                </>
              )}
            </dl>
          </Card>
          <ButtonGroup>
            {/* Step 5: leaving puts focus back on the control this was opened from. */}
            <Button
              iconName="chevron-left"
              onClick={() => {
                setOpenAlertId(null)
                returnFocusTo(openAlert.id)
              }}
            >
              {t('Back to the dashboard')}
            </Button>
          </ButtonGroup>
        </section>
      )}

      <section aria-labelledby="dash-report" className="journey-section">
        <h2 id="dash-report">{t('7. Sales by category')}</h2>
        <p>{t(`For ${SALES_REPORT.period}.`)}</p>
        <DataTable
          caption={t('Taxable value by garment category')}
          columns={[
            {
              id: 'category',
              header: t('Category'),
              primary: true,
              cell: (row) => t(row.category),
            },
            {
              id: 'invoices',
              header: t('Invoices'),
              numeric: true,
              cell: (row) => formatters.formatNumber(row.invoices),
            },
            {
              id: 'taxable',
              header: t('Taxable value'),
              numeric: true,
              cell: (row) => (
                <span className="journey-amount">{formatters.formatMoney(row.taxableValue)}</span>
              ),
            },
          ]}
          rowKey={(row) => row.id}
          rowLabel={(row) => t(row.category)}
          rows={SALES_REPORT.rows}
        />

        {/*
          The reconciliation footer. A definition list rather than a table foot, because every
          figure needs its own label read with it: step 7 is "hear the totals labelled", and a total
          found by counting cells across a row is a total somebody will get wrong.
        */}
        <Card headingLevel={3} title={t('Reconciliation')}>
          <dl className="journey-summary">
            <dt>{t('Taxable value')}</dt>
            <dd className="journey-amount">{formatters.formatMoney(SALES_REPORT.taxableValue)}</dd>
            <dt>{t('Central GST')}</dt>
            <dd className="journey-amount">{formatters.formatMoney(SALES_REPORT.centralTax)}</dd>
            <dt>{t('State GST')}</dt>
            <dd className="journey-amount">{formatters.formatMoney(SALES_REPORT.stateTax)}</dd>
            <dt>{t('Round-off')}</dt>
            <dd className="journey-amount">{formatters.formatMoney(SALES_REPORT.roundOff)}</dd>
            <dt>{t('Total invoiced')}</dt>
            <dd className="journey-amount journey-total">
              {formatters.formatMoney(SALES_REPORT.total)}
            </dd>
          </dl>
        </Card>
      </section>
    </section>
  )
}
