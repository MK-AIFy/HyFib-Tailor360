import { useState } from 'react'
import { useShellStatus } from '../../components/layout/useShellStatus'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ButtonGroup } from '../../components/primitives/ButtonGroup'
import { Card } from '../../components/primitives/Card'
import { StatusBadge } from '../../components/primitives/StatusBadge'
import { useDemoText } from '../../components/primitives/demoText'
import { EmptyState } from '../../components/states/EmptyState'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { Select } from '../../design-system/components/forms/Select'
import { Switch } from '../../design-system/components/forms/Switch'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import { getFormatters } from '../../i18n/formatters'
import { NOW, PHASES, STAFF } from '../fixtures/branch'
import { STOCK_ITEMS } from '../fixtures/inventory'
import { MY_JOBS, SUPERSEDED_BARCODE, resolveScan } from '../fixtures/jobs'
import type { JourneyJob } from '../fixtures/jobs'

/** The phase after this one, or null at the end of the route. */
function nextPhase(phase: string): string | null {
  const index = PHASES.indexOf(phase as (typeof PHASES)[number])
  return index < 0 || index === PHASES.length - 1 ? null : (PHASES[index + 1] ?? null)
}

/**
 * `A11Y-RJ-03` — Tailor: scan in, work a phase, scan out.
 *
 * All nine steps of its record in docs/nfr/a11y-checklist.md section 6.8 are reachable here. The
 * three that decide whether the screen is any good on a shop floor are the last three.
 *
 * ## The scan is a status channel, not a toast
 *
 * Every scan result goes to `announceScan`, which the shell renders in a reserved region at the top
 * of `main` and keeps there until it is superseded. A rejection is announced assertively and names
 * **which** rule failed — a superseded label is a different sentence from an unknown one, because
 * the remedy is different — and an acceptance is announced politely with the next expected action.
 * A toast would be gone before somebody holding a garment had read it, which is why section 6 of
 * docs/nfr/accessibility-localisation.md forbids one here.
 *
 * ## A shortage is a hold, and a hold has a reason
 *
 * Recording a shortage does not fail silently and does not merely colour the row: it puts the job on
 * hold, and the hold carries the reason in words. Checklist item A11Y-53 asks for the reason rather
 * than a code, and the person who has to clear it tomorrow is the one who needs the sentence.
 *
 * ## Offline queues the scan, and says what that means
 *
 * Completing a phase offline is one of the few things this product will queue — an idempotent scan
 * submission, which is exactly what #51's bounded queue is for. So the screen says it is queued
 * rather than saved, and reconnecting reports the **replay outcome** rather than assuming it. The
 * client never asserts what the server has not confirmed.
 *
 * There is no backend: the state is `useState` over the fixtures.
 */
export function TailorQueueScreen() {
  const t = useDemoText()
  const formatters = getFormatters()
  const status = useShellStatus()

  const [payload, setPayload] = useState('')
  const [openJob, setOpenJob] = useState<JourneyJob | null>(null)
  const [phaseStarted, setPhaseStarted] = useState(false)
  const [itemId, setItemId] = useState(STOCK_ITEMS[0]?.id ?? '')
  const [quantity, setQuantity] = useState(0.25)
  const [shortageReason, setShortageReason] = useState('')
  const [held, setHeld] = useState<string | null>(null)
  const [online, setOnline] = useState(true)
  const [queued, setQueued] = useState(false)
  const [completed, setCompleted] = useState<string | null>(null)
  const [custodian, setCustodian] = useState<string | null>(null)

  const item = STOCK_ITEMS.find((candidate) => candidate.id === itemId)

  /** Resolves a payload and publishes the outcome, naming the rule when it refuses. */
  function scan(value: string) {
    const resolution = resolveScan(value)

    if (resolution.outcome === 'superseded') {
      status.announceScan({
        outcome: 'rejected',
        message: t(
          `That label was reprinted on ${formatters.formatShortDate('2026-05-05T09:00:00+05:30')} and is no longer the job's label. Use the reprinted label ${resolution.replacedBy}.`,
        ),
      })
      return
    }

    if (resolution.outcome === 'unknown') {
      status.announceScan({
        outcome: 'rejected',
        message: t(
          'No job in this branch carries that label. Check the label, or open the job from the queue and enter the number by hand with a reason.',
        ),
      })
      return
    }

    const { job } = resolution
    setOpenJob(job)
    setPhaseStarted(false)
    setCompleted(null)
    setCustodian(null)
    setHeld(job.holdReason ?? null)
    status.announceScan({
      outcome: 'accepted',
      message: t(
        `${job.id}, ${job.garment}, ${job.phase}. Next: start the phase, then record any material you take.`,
      ),
    })
  }

  return (
    <section className="page journey-screen">
      <h1>{t('My work')}</h1>
      <p>
        {t(STAFF.tailor)} · {formatters.formatDateTime(NOW)}
      </p>

      <section aria-labelledby="tailor-scan" className="journey-section">
        <h2 id="tailor-scan">{t('1. Scan a label')}</h2>
        <TextField
          description={t(
            'The wedge scanner types into this field. It never takes the focus away from what you are doing, so you can also type a number here.',
          )}
          enterKeyHint="go"
          label={t('Label or job number')}
          name="scanPayload"
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
            {t('Scan in')}
          </Button>
          {/* Step 8: a scan that must be rejected. Two rejections, two different sentences. */}
          <Button
            onClick={() => {
              scan(SUPERSEDED_BARCODE)
            }}
          >
            {t('Scan the reprinted label')}
          </Button>
        </ButtonGroup>
      </section>

      <section aria-labelledby="tailor-queue" className="journey-section">
        <h2 id="tailor-queue">{t('2. My queue')}</h2>
        {MY_JOBS.length === 0 ? (
          <EmptyState headingLevel={3} title={t('Nothing assigned to you yet')}>
            {t('Jobs appear here when the workboard assigns them to you.')}
          </EmptyState>
        ) : (
          <ul className="journey-list">
            {MY_JOBS.map((job) => (
              <li key={job.id}>
                <Card
                  headingLevel={3}
                  meta={
                    <StatusBadge
                      detail={formatters.formatRelativeTime(job.due, NOW).relative}
                      status={job.status}
                    />
                  }
                  selected={openJob?.id === job.id}
                  title={t(job.garment)}
                  actions={
                    <ButtonGroup>
                      <Button
                        onClick={() => {
                          scan(job.barcode)
                        }}
                        variant="primary"
                      >
                        {t(`Open ${job.id}`)}
                      </Button>
                    </ButtonGroup>
                  }
                >
                  <p className="journey-row__meta">
                    {job.id} · {t(job.customer)} · {t(job.phase)}
                  </p>
                  <p className="journey-row__meta">
                    {t('Due')} {formatters.formatShortDate(job.due)} (
                    {formatters.formatRelativeTime(job.due, NOW).relative})
                  </p>
                </Card>
              </li>
            ))}
          </ul>
        )}
      </section>

      {openJob === null ? null : (
        <>
          <section aria-labelledby="tailor-job" className="journey-section">
            <h2 id="tailor-job">{t('3. The job in hand')}</h2>
            <Card
              headingLevel={3}
              meta={<StatusBadge prominent status={openJob.status} />}
              raised
              title={`${openJob.id} — ${t(openJob.garment)}`}
            >
              <dl className="journey-summary">
                <dt>{t('Customer')}</dt>
                <dd>{t(openJob.customer)}</dd>
                <dt>{t('Phase')}</dt>
                <dd>{t(openJob.phase)}</dd>
                <dt>{t('Assigned to')}</dt>
                <dd>{t(openJob.assignee ?? 'Nobody yet')}</dd>
                <dt>{t('Custodian')}</dt>
                <dd>{t(custodian ?? STAFF.tailor)}</dd>
              </dl>
            </Card>

            {held === null ? null : (
              <Alert live="polite" title={t('This job is on hold')} tone="warning">
                {t(held)}
              </Alert>
            )}

            <ButtonGroup size="primary">
              <Button
                busy={false}
                iconName="play"
                onClick={() => {
                  setPhaseStarted(true)
                  status.announceAutosave(
                    t(
                      `${openJob.phase} started on ${openJob.id}, assigned to ${openJob.assignee ?? STAFF.tailor}.`,
                    ),
                  )
                }}
                size="primary"
                variant="primary"
              >
                {t(`Start ${openJob.phase}`)}
              </Button>
            </ButtonGroup>
          </section>

          {!phaseStarted ? null : (
            <>
              <section aria-labelledby="tailor-material" className="journey-section">
                <h2 id="tailor-material">{t('4. Material against this phase')}</h2>
                <div className="journey-fields" data-columns="2">
                  <Select
                    emptyLabel={null}
                    label={t('Item')}
                    name="materialItem"
                    onValueChange={setItemId}
                    options={STOCK_ITEMS.map((stock) => ({
                      value: stock.id,
                      label: t(`${stock.name} (${stock.unitLabel})`),
                    }))}
                    value={itemId}
                  />
                  <NumericStepper
                    decimalPlaces={2}
                    label={t('Quantity taken')}
                    min={0}
                    name="materialQuantity"
                    onValueChange={setQuantity}
                    step={0.25}
                    unit={{
                      symbol: item?.unitSymbol ?? '',
                      label: t(item?.unitLabel ?? ''),
                      position: 'trailing',
                    }}
                    value={quantity}
                  />
                </div>
                <ButtonGroup>
                  <Button
                    iconName="package"
                    onClick={() => {
                      status.announceAutosave(
                        t(
                          `${formatters.formatQuantity(quantity, item?.unitSymbol ?? '')} of ${item?.name ?? ''} recorded against ${openJob.id}, ${openJob.phase}.`,
                        ),
                      )
                    }}
                    variant="primary"
                  >
                    {t('Record this material')}
                  </Button>
                </ButtonGroup>
              </section>

              <section aria-labelledby="tailor-shortage" className="journey-section">
                <h2 id="tailor-shortage">{t('5. Or record a shortage')}</h2>
                <TextArea
                  description={t(
                    'What is short, and how much. Whoever clears the hold tomorrow reads this sentence, not a code.',
                  )}
                  label={t('What is short')}
                  name="shortageReason"
                  onValueChange={setShortageReason}
                  rows={3}
                  value={shortageReason}
                />
                <ButtonGroup>
                  <Button
                    iconName="pause"
                    onClick={() => {
                      const reason =
                        shortageReason.trim() === ''
                          ? t(`${item?.name ?? 'Material'} is short.`)
                          : shortageReason
                      setHeld(reason)
                      status.announceSync({
                        tone: 'warning',
                        message: t(`${openJob.id} is on hold. ${reason}`),
                      })
                    }}
                  >
                    {t('Put the job on hold')}
                  </Button>
                </ButtonGroup>
              </section>

              <section aria-labelledby="tailor-finish" className="journey-section">
                <h2 id="tailor-finish">{t('6. Finish and hand over')}</h2>

                {/* Story control. The application reads the real connection; #51 owns the queue. */}
                <Switch
                  description={t(
                    'A completed phase is one of the few things this product will queue. A payment is not.',
                  )}
                  label={t('Working offline')}
                  name="offline"
                  onValueChange={(next) => {
                    setOnline(!next)
                    if (next) {
                      status.announceSync({
                        tone: 'info',
                        message: t(
                          'Offline. Scans are queued on this device and sent when you reconnect.',
                        ),
                      })
                    }
                  }}
                  value={!online}
                />

                <ButtonGroup size="primary">
                  <Button
                    iconName="check"
                    onClick={() => {
                      const following = nextPhase(openJob.phase)
                      if (online) {
                        setQueued(false)
                        setCompleted(following)
                        status.announceSync({
                          tone: 'success',
                          message: t(
                            following === null
                              ? `${openJob.phase} complete on ${openJob.id}. That was the last phase; the job goes to the ready shelf.`
                              : `${openJob.phase} complete on ${openJob.id}. Next phase: ${following}.`,
                          ),
                        })
                        return
                      }
                      setQueued(true)
                      status.announceSync({
                        tone: 'info',
                        message: t(
                          `Queued on this device: ${openJob.phase} complete on ${openJob.id}. It has not been sent yet, and nothing is confirmed until it is.`,
                        ),
                      })
                    }}
                    size="primary"
                    variant="primary"
                  >
                    {t(`Complete ${openJob.phase}`)}
                  </Button>

                  <Button
                    iconName="truck"
                    onClick={() => {
                      setCustodian(STAFF.tailorMaster)
                      status.announceScan({
                        outcome: 'accepted',
                        message: t(
                          `${openJob.id} handed over. Custodian is now ${STAFF.tailorMaster}.`,
                        ),
                      })
                    }}
                    size="primary"
                  >
                    {t('Scan out')}
                  </Button>
                </ButtonGroup>

                {!queued ? null : (
                  <Alert
                    actions={
                      <Button
                        iconName="refresh"
                        onClick={() => {
                          setOnline(true)
                          setQueued(false)
                          setCompleted(nextPhase(openJob.phase))
                          status.announceSync({
                            tone: 'success',
                            message: t(
                              `Reconnected. 1 queued scan replayed and accepted: ${openJob.phase} complete on ${openJob.id}.`,
                            ),
                          })
                        }}
                      >
                        {t('Reconnect and send')}
                      </Button>
                    }
                    live="polite"
                    title={t('1 scan waiting on this device')}
                    tone="info"
                  >
                    {t(
                      'It is stored on this device only. It is not on the server, nobody else can see it, and it is not confirmed until it has been sent and accepted.',
                    )}
                  </Alert>
                )}

                {completed === null ? null : (
                  <Alert live="polite" title={t('Phase complete')} tone="success">
                    {t(`${openJob.id} moves to ${completed}.`)}
                  </Alert>
                )}
              </section>
            </>
          )}
        </>
      )}
    </section>
  )
}
