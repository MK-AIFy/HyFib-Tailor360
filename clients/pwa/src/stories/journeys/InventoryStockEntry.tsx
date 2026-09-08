import { useState } from 'react'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { useShellStatus } from '../../components/layout/useShellStatus'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ButtonGroup } from '../../components/primitives/ButtonGroup'
import { Card } from '../../components/primitives/Card'
import { DataTable } from '../../components/primitives/DataTable'
import { useDemoText } from '../../components/primitives/demoText'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { Select } from '../../design-system/components/forms/Select'
import { Switch } from '../../design-system/components/forms/Switch'
import { TextField } from '../../design-system/components/forms/TextField'
import { getFormatters } from '../../i18n/formatters'
import { NOW, PHASES, STAFF } from '../fixtures/branch'
import {
  LEDGER,
  STOCKTAKE,
  STOCK_ITEMS,
  SUPPLIERS,
  purchaseUnitsFor,
  resolveStockScan,
  variance,
} from '../fixtures/inventory'
import { JOBS } from '../fixtures/jobs'

/**
 * `A11Y-RJ-05` — Inventory Clerk: receive, issue against a job, and count.
 *
 * Its own record in docs/nfr/a11y-checklist.md section 6.8, all nine steps reachable here.
 *
 * ## A quantity is never a bare number
 *
 * Every quantity on this screen carries its unit, and every purchase carries its conversion in
 * words: "2 rolls of 25 m — 50 metres into stock". A clerk receiving in rolls and issuing in metres
 * is where a stock ledger goes wrong silently, and the conversion is exactly the thing a screen
 * reader user cannot infer from the layout.
 *
 * ## The scan tells you which rule refused it
 *
 * Three outcomes, three sentences: resolved, a `G-` garment label scanned into a stock field, and a
 * payload no item carries. The middle one is not a hypothetical — it is what happens with a garment
 * in the other hand, and "not found" would send somebody looking for a missing item that is not
 * missing.
 *
 * ## Count and recount are two fields, and the variance is a pair
 *
 * Step 5 asks to hear the two fields apart and step 6 asks for the variance "with its sign and its
 * counted-versus-expected pair". A single field that overwrote the first count would make the
 * recount worthless, and a variance printed alone — "−1.5" — is a number nobody can act on.
 *
 * ## Posting is online-only, and says so
 *
 * A stock posting is not queued. The client guide is explicit that inventory reconciliation is
 * online-only with an explicit blocked-action state, and the blocked state says "this will not be
 * queued" rather than leaving somebody to discover it tomorrow.
 *
 * There is no backend: the state is `useState` over the fixtures.
 */
export function InventoryStockEntryScreen() {
  const t = useDemoText()
  const formatters = getFormatters()
  const status = useShellStatus()

  const [supplierId, setSupplierId] = useState<string>(SUPPLIERS[0]?.id ?? '')
  const [receiveItemId, setReceiveItemId] = useState<string>(STOCK_ITEMS[0]?.id ?? '')
  const [purchaseUnit, setPurchaseUnit] = useState<string>('roll')
  const [purchaseQuantity, setPurchaseQuantity] = useState(2)

  const [payload, setPayload] = useState('')
  const [scannedItemId, setScannedItemId] = useState<string | null>(null)

  const [issueJobId, setIssueJobId] = useState<string>(JOBS[0]?.id ?? '')
  const [issuePhase, setIssuePhase] = useState<string>(PHASES[1] ?? 'Stitching')
  const [issueQuantity, setIssueQuantity] = useState(0.25)
  const [confirmingIssue, setConfirmingIssue] = useState(false)

  const [counts, setCounts] = useState<Readonly<Record<string, number>>>({})
  const [recounts, setRecounts] = useState<Readonly<Record<string, number>>>({})
  const [confirmingPost, setConfirmingPost] = useState(false)
  const [posted, setPosted] = useState(false)

  const [online, setOnline] = useState(true)

  const receiveItem = STOCK_ITEMS.find((item) => item.id === receiveItemId)

  /*
   * The units offered are the ones that convert into *this* item's stocked unit, and the selected
   * one falls back to the first of them whenever the item changes underneath it. Derived rather than
   * corrected in an effect: an effect that re-set the selection would render one frame of "2 rolls
   * of 25 m is 50 card" before fixing itself, and that frame is the whole defect.
   */
  const availableUnits = purchaseUnitsFor(receiveItem)
  const unit =
    availableUnits.find((candidate) => candidate.value === purchaseUnit) ?? availableUnits[0]
  const baseQuantity = purchaseQuantity * (unit?.baseUnitsEach ?? 1)
  const scannedItem = STOCK_ITEMS.find((item) => item.id === scannedItemId)
  const issueItem = scannedItem ?? receiveItem

  /** The stocktake lines with whatever has been counted on this screen laid over the fixture. */
  const lines = STOCKTAKE.lines.map((line) => ({
    ...line,
    counted: counts[line.itemId] ?? line.counted,
    recounted: recounts[line.itemId] ?? line.recounted,
  }))

  function scan(value: string) {
    const resolution = resolveStockScan(value)

    if (resolution.outcome === 'wrong-namespace') {
      status.announceScan({
        outcome: 'rejected',
        message: t(
          'That is a garment label, not a stock label. A stock label starts with S. Scan the label on the rack, or choose the item from the list.',
        ),
      })
      return
    }

    if (resolution.outcome === 'unknown') {
      status.announceScan({
        outcome: 'rejected',
        message: t('No stock item in this branch carries that label. Check the label on the rack.'),
      })
      return
    }

    setScannedItemId(resolution.item.id)
    status.announceScan({
      outcome: 'accepted',
      message: t(
        `${resolution.item.name}, ${formatters.formatQuantity(resolution.item.onHand, resolution.item.unitSymbol)} on hand in ${resolution.item.location}.`,
      ),
    })
  }

  return (
    <section className="page journey-screen">
      <h1>{t('Stock')}</h1>
      <p>
        {t(STAFF.inventory)} · {formatters.formatDateTime(NOW)}
      </p>

      <section aria-labelledby="stock-receive" className="journey-section">
        <h2 id="stock-receive">{t('1. Receive a purchase')}</h2>
        <div className="journey-fields" data-columns="2">
          <Select
            emptyLabel={null}
            label={t('Supplier')}
            name="supplier"
            onValueChange={setSupplierId}
            options={SUPPLIERS.map((supplier) => ({
              value: supplier.id,
              label: t(supplier.name),
            }))}
            value={supplierId}
          />
          <Select
            emptyLabel={null}
            label={t('Item')}
            name="receiveItem"
            onValueChange={setReceiveItemId}
            options={STOCK_ITEMS.map((item) => ({
              value: item.id,
              label: t(`${item.name} — stocked in ${item.unitLabel}`),
            }))}
            value={receiveItemId}
          />
          <NumericStepper
            decimalPlaces={2}
            label={t('Quantity received')}
            min={0}
            name="purchaseQuantity"
            onValueChange={setPurchaseQuantity}
            step={1}
            value={purchaseQuantity}
          />
          <Select
            description={t(
              'The unit on the supplier’s note, which need not be the stocked unit. Only the units that convert into this item are offered.',
            )}
            emptyLabel={null}
            label={t('Received in')}
            name="purchaseUnit"
            onValueChange={setPurchaseUnit}
            options={availableUnits.map((candidate) => ({
              value: candidate.value,
              label: t(candidate.label),
            }))}
            value={unit?.value ?? ''}
          />
        </div>

        {/* The conversion in words. Never left for the reader to infer from two adjacent numbers. */}
        <Alert live="polite" tone="info">
          {t(
            `${formatters.formatNumber(purchaseQuantity, 2)} × ${unit?.label ?? ''} is ${formatters.formatQuantity(baseQuantity, receiveItem?.unitSymbol ?? '')} of ${receiveItem?.name ?? ''} into ${receiveItem?.location ?? ''}.`,
          )}
        </Alert>

        <ButtonGroup>
          <Button
            iconName="plus"
            onClick={() => {
              status.announceAutosave(
                t(
                  `Receipt recorded: ${formatters.formatQuantity(baseQuantity, receiveItem?.unitSymbol ?? '')} of ${receiveItem?.name ?? ''} from ${SUPPLIERS.find((supplier) => supplier.id === supplierId)?.name ?? ''}.`,
                ),
              )
            }}
            variant="primary"
          >
            {t('Record the receipt')}
          </Button>
        </ButtonGroup>
      </section>

      <section aria-labelledby="stock-scan" className="journey-section">
        <h2 id="stock-scan">{t('2. Or find the item by scanning it')}</h2>
        <TextField
          description={t('A stock label starts with S. The wedge scanner types into this field.')}
          enterKeyHint="go"
          label={t('Stock label')}
          name="stockPayload"
          onValueChange={setPayload}
          value={payload}
        />
        <ButtonGroup size="primary">
          <Button
            iconName="scan"
            onClick={() => {
              scan(payload)
            }}
            size="primary"
            variant="primary"
          >
            {t('Scan the label')}
          </Button>
          <Button
            onClick={() => {
              scan('G-6MTB4XZ9DKQ2')
            }}
          >
            {t('Scan a garment label by mistake')}
          </Button>
        </ButtonGroup>

        {scannedItem === undefined ? null : (
          <Card headingLevel={3} title={t(scannedItem.name)}>
            <dl className="journey-summary">
              <dt>{t('On hand')}</dt>
              <dd className="journey-amount">
                {formatters.formatQuantity(scannedItem.onHand, scannedItem.unitSymbol)}
              </dd>
              <dt>{t('Location')}</dt>
              <dd>{t(scannedItem.location)}</dd>
            </dl>
          </Card>
        )}
      </section>

      <section aria-labelledby="stock-issue" className="journey-section">
        <h2 id="stock-issue">{t('3. Issue against a job')}</h2>
        <div className="journey-fields" data-columns="2">
          <Select
            emptyLabel={null}
            label={t('Job')}
            name="issueJob"
            onValueChange={setIssueJobId}
            options={JOBS.map((job) => ({ value: job.id, label: `${job.id} — ${t(job.garment)}` }))}
            value={issueJobId}
          />
          <Select
            emptyLabel={null}
            label={t('Phase')}
            name="issuePhase"
            onValueChange={setIssuePhase}
            options={PHASES.map((phase) => ({ value: phase, label: t(phase) }))}
            value={issuePhase}
          />
          <NumericStepper
            decimalPlaces={2}
            label={t('Quantity issued')}
            min={0}
            name="issueQuantity"
            onValueChange={setIssueQuantity}
            step={0.25}
            unit={{
              symbol: issueItem?.unitSymbol ?? '',
              label: t(issueItem?.unitLabel ?? ''),
              position: 'trailing',
            }}
            value={issueQuantity}
          />
        </div>
        <ButtonGroup>
          <Button
            iconName="package"
            onClick={() => {
              setConfirmingIssue(true)
            }}
            variant="primary"
          >
            {t('Issue this material')}
          </Button>
        </ButtonGroup>
      </section>

      <section aria-labelledby="stock-count" className="journey-section">
        <h2 id="stock-count">{t('4. Stocktake')}</h2>
        <p>
          {STOCKTAKE.id} · {t(STOCKTAKE.location)}.{' '}
          {t(
            STOCKTAKE.frozen
              ? 'This location is frozen while the count is open: nothing can be issued from it until the count is posted.'
              : 'This location is open.',
          )}
        </p>

        {lines.map((line) => {
          const difference = variance(line)
          return (
            <Card headingLevel={3} key={line.itemId} title={t(line.name)}>
              <div className="journey-fields" data-columns="2">
                <NumericStepper
                  decimalPlaces={2}
                  description={t('What you counted the first time. It is kept.')}
                  label={t('First count')}
                  min={0}
                  name={`count-${line.itemId}`}
                  onValueChange={(next) => {
                    setCounts((previous) => ({ ...previous, [line.itemId]: next }))
                  }}
                  step={0.5}
                  unit={{ symbol: line.unitSymbol, label: line.unitSymbol, position: 'trailing' }}
                  value={line.counted}
                />
                <NumericStepper
                  decimalPlaces={2}
                  description={t('A separate field, so the two counts can be compared.')}
                  label={t('Recount')}
                  min={0}
                  name={`recount-${line.itemId}`}
                  onValueChange={(next) => {
                    setRecounts((previous) => ({ ...previous, [line.itemId]: next }))
                  }}
                  step={0.5}
                  unit={{ symbol: line.unitSymbol, label: line.unitSymbol, position: 'trailing' }}
                  value={line.recounted}
                />
              </div>
              {/* The variance as a pair, with its sign spelled out rather than left to a minus glyph. */}
              <p>
                {t(
                  `${difference === 0 ? 'No variance' : difference > 0 ? 'Over by' : 'Short by'} ${difference === 0 ? '' : formatters.formatQuantity(Math.abs(difference), line.unitSymbol)}: counted ${formatters.formatQuantity(line.recounted, line.unitSymbol)} against ${formatters.formatQuantity(line.expected, line.unitSymbol)} expected.`,
                )}
              </p>
            </Card>
          )
        })}

        {/* Story control. The application reads the real connection state. */}
        <Switch
          label={t('Working offline')}
          name="offline"
          onValueChange={(next) => {
            setOnline(!next)
          }}
          value={!online}
        />

        {online ? (
          <ButtonGroup size="primary">
            <Button
              iconName="check"
              onClick={() => {
                setConfirmingPost(true)
              }}
              size="primary"
              variant="primary"
            >
              {t('Post the stocktake')}
            </Button>
          </ButtonGroup>
        ) : (
          <OfflineBlockedAction action={t('posting a stocktake')} online={false}>
            {t(
              'A stock posting moves the ledger, so it is never queued on a device: it would be applied against a stock level that had already moved. Reconnect and post it then.',
            )}
          </OfflineBlockedAction>
        )}

        {!posted ? null : (
          <Alert live="polite" title={t('Stocktake posted')} tone="success">
            {t(
              `${STOCKTAKE.id} is posted and ${STOCKTAKE.location} is open again. The adjustments are on the ledger and cannot be edited; a correction is another posting.`,
            )}
          </Alert>
        )}
      </section>

      <section aria-labelledby="stock-ledger" className="journey-section">
        <h2 id="stock-ledger">{t('8. The stock ledger')}</h2>
        <DataTable
          caption={t('Stock movements, newest first')}
          columns={[
            { id: 'id', header: t('Entry'), primary: true, cell: (row) => row.id },
            {
              id: 'at',
              header: t('When'),
              cell: (row) => formatters.formatDateTime(row.at),
            },
            { id: 'item', header: t('Item'), cell: (row) => t(row.itemName) },
            {
              id: 'quantity',
              header: t('Quantity'),
              numeric: true,
              // The direction is a word, because a leading minus is the easiest thing to miss.
              cell: (row) => (
                <span className="journey-amount">
                  {t(row.quantity < 0 ? 'Out' : 'In')}{' '}
                  {formatters.formatQuantity(Math.abs(row.quantity), row.unitSymbol)}
                </span>
              ),
            },
            { id: 'movement', header: t('Movement'), cell: (row) => t(row.movement) },
            {
              id: 'against',
              header: t('Against'),
              hideWhenNarrow: true,
              cell: (row) => t(row.against),
            },
            {
              id: 'actor',
              header: t('By'),
              hideWhenNarrow: true,
              cell: (row) => t(row.actor),
            },
          ]}
          rowKey={(row) => row.id}
          rowLabel={(row) => `${t('ledger entry')} ${row.id}`}
          rows={LEDGER}
        />
      </section>

      <ConfirmDialog
        action="issuing this material"
        confirmLabel={t('Issue it')}
        onCancel={() => {
          setConfirmingIssue(false)
        }}
        onConfirm={() => {
          setConfirmingIssue(false)
          status.announceAutosave(
            t(
              `Issued ${formatters.formatQuantity(issueQuantity, issueItem?.unitSymbol ?? '')} of ${issueItem?.name ?? ''} to ${issueJobId}, ${issuePhase}.`,
            ),
          )
        }}
        open={confirmingIssue}
        tier="confirm"
        title={t('Issue this material?')}
      >
        {/* All three read back before it commits: item and quantity, job, and phase. */}
        {t(
          `${formatters.formatQuantity(issueQuantity, issueItem?.unitSymbol ?? '')} of ${issueItem?.name ?? ''} goes out of ${issueItem?.location ?? ''} against ${issueJobId}, ${issuePhase}.`,
        )}
      </ConfirmDialog>

      <ConfirmDialog
        action="posting this stocktake"
        confirmLabel={t('Post the stocktake')}
        irreversible
        onCancel={() => {
          setConfirmingPost(false)
        }}
        onConfirm={() => {
          setConfirmingPost(false)
          setPosted(true)
        }}
        open={confirmingPost}
        tier="reason"
        title={t('Post this stocktake?')}
      >
        {t(
          `The counted quantities become the stock on hand in ${STOCKTAKE.location}, and every difference is written to the ledger as an adjustment. The ledger is append-only: this cannot be edited or undone, only corrected by another posting.`,
        )}
      </ConfirmDialog>
    </section>
  )
}
