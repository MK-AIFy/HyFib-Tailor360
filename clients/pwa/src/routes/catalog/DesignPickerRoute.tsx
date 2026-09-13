import { useCallback, useEffect, useRef, useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import type { IntlShape } from 'react-intl'
import { useNavigate, useParams } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { ApiError } from '../../auth/apiClient'
import type { VersionedResponse } from '../../auth/apiClient'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { Dialog } from '../../components/dialogs/Dialog'
import { EmptyState } from '../../components/states/EmptyState'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { Checkbox } from '../../design-system/components/forms/Checkbox'
import { TextArea } from '../../design-system/components/forms/TextArea'
import {
  checkCatalogDesignDraft,
  migrateCatalogDesignDraft,
  readCatalogDesignDraft,
  readCatalogDesignPicker,
  saveCatalogDesignDraft,
  startCatalogDesignDraft,
} from '../../catalog/catalogApi'
import { dropsAChoice } from '../../catalog/designMigration'
import { evaluateDesignPickerEffects } from '../../catalog/designPickerEffects'
import type { DesignPickerSelections } from '../../catalog/designPickerEffects'
import {
  operandGroupName,
  operandOptionNames,
  optionName,
} from '../../catalog/designPickerRuleText'
import type {
  CatalogDesignOperand,
  DesignAutoSelection,
  DesignCheck,
  DesignDraftSelection,
  DesignMigrationPrompt,
  DesignPicker,
  DesignPickerGroup,
  DesignSelectionDraft,
  SaveDesignSelectionsRequest,
} from '../../catalog/types'
import { DesignOptionCard } from './DesignOptionCard'
import { IllustrationPreview } from './IllustrationPreview'
import './designPicker.css'

/**
 * The design picker (#142): what a service type offers, chosen against the rules that hold between
 * its options, saved and re-validated by the server on every change.
 *
 * ## Why every change is saved and checked, not merely held
 *
 * `docs/prd/design-options.md` section 4 rule 6 is explicit: the server's evaluation is authoritative
 * and the client's own reading of a rule is a convenience. So the source of truth this screen shows —
 * which options are excluded, which group still needs an answer, what a `requires` rule already
 * settled — is `…/check`'s answer after the whole selection set has been saved, not a client-side
 * re-implementation of `DesignRuleEngine.Evaluate`'s fixed point. `designPickerEffects.ts` runs a
 * narrower, single-antecedent version of the same test purely to grey out a card before the network
 * has had a chance to say so; everything that decides whether the summary is clean comes from `check`.
 *
 * ## Why a save can trigger another save
 *
 * A `requires` rule whose consequent has exactly one admissible option is satisfied by selecting it
 * (section 4 rule 9), and `check` reports it as `autoSelections` rather than mutating the draft
 * itself. This screen folds that option into its own selections and lets the debounce effect below
 * save it — which is what actually makes the auto-selection real rather than merely displayed. The
 * fixed point this converges to is small: a rule already satisfied is never reported as an
 * auto-selection twice.
 */
export function DesignPickerRoute() {
  const intl = useIntl()
  const navigate = useNavigate()
  const network = useNetworkState()
  const { serviceTypeId, draftId } = useParams()

  const [startFailure, setStartFailure] = useState<unknown>(null)
  const startKeyRef = useRef(crypto.randomUUID())

  useEffect(() => {
    if (draftId !== undefined || serviceTypeId === undefined || !network.online) {
      return
    }
    let cancelled = false
    startCatalogDesignDraft({ body: { serviceTypeId }, idempotencyKey: startKeyRef.current })
      .then((started) => {
        if (!cancelled) {
          void navigate(
            `/catalog/design/${serviceTypeId}/${started.value.designSelectionDraftId}`,
            { replace: true },
          )
        }
      })
      .catch((cause: unknown) => {
        if (!cancelled) {
          setStartFailure(cause)
        }
      })
    return () => {
      cancelled = true
    }
  }, [draftId, navigate, network.online, serviceTypeId])

  const [reloads, setReloads] = useState(0)
  // Which generation `draft.value` actually answers for — distinct from `reloads` itself, which
  // changes the instant a reload is merely requested, before its `GET` has landed. A resolved
  // override below must wait for this rather than for the request, or a second reload started
  // before the first one lands would retire the override immediately and expose whatever
  // `draft.value` still holds, which can be stale all the way back to before the first reload.
  const [draftValueGeneration, setDraftValueGeneration] = useState(0)
  const draft = useAdminResource(
    `design-draft:${draftId ?? ''}:${String(reloads)}`,
    async (signal) => {
      const requestedGeneration = reloads
      const result = await readCatalogDesignDraft(draftId ?? '', signal)
      setDraftValueGeneration(requestedGeneration)
      return result
    },
  )

  /**
   * A republish gives every service type in the new version a fresh row id — the pinned
   * `draft.serviceTypeId` a `migrationPrompt` reports on stops resolving against `/current` the
   * moment any newer version publishes, whatever this draft's own category did. So the picker read
   * below is never attempted while a migration is pending: reading it would 404 on an id the current
   * catalogue no longer has, and gating the whole screen on that read — as this once did — is what
   * kept Reception from ever seeing the migration choice at all. It reads again, against the fresh
   * id, only once the draft is either migrated or was never pinned to a superseded version.
   */
  // `DesignPickerBody`'s own autosave notices a republish mid-session by re-reading the draft
  // itself (`readCatalogDesignDraft`'s answer is the only place that fact appears). That read has
  // already succeeded by the time it calls back here — asking `draft.reload()` to fetch it a
  // second time would, if that second read failed, leave `draft.value` on its old pre-migration
  // value (by design: a reload keeps the previous value on screen) with no way to surface the
  // prompt this screen already has. Holding it directly sidesteps that second, avoidable read. It
  // carries the read's own version alongside the prompt — that autosave already advanced the
  // draft's ETag, and the migration gate must send that precondition, not the stale one `draft.value`
  // still holds — and it is scoped to the draftId it was captured for, since this route component
  // is not remounted on a client-side navigation to a different draft.
  const [forcedMigration, setForcedMigration] = useState<{
    readonly draftId: string
    readonly prompt: DesignMigrationPrompt
    readonly version: string
    readonly hasReferenceImage: boolean
  } | null>(null)
  // A migrated draft, from the migrate response itself rather than a further `GET`: that read
  // would still answer with the pre-migration draft until it resolved, and a slow or failed one
  // would either flash the stale picker (a remounted body autosaving against the old ETag) back
  // into view or leave the gate stuck up. `reloads` is still bumped alongside it so `draft` itself
  // catches up in the background — and this override steps aside once `draftValueGeneration` shows
  // that catch-up has actually landed, so a later conflict's own "read it again" isn't stuck behind
  // a migration from earlier in the same session. It cannot step aside merely because `reloads` has
  // moved on again (a second reload requested before the first lands): `draft.value` would still be
  // holding whatever it held before either reload, which can be stale all the way back to before
  // the migration. `reloadGeneration` is the value `reloads` will hold once that first bump lands,
  // and it is the only generation this override answers for.
  const [resolvedDraft, setResolvedDraft] = useState<{
    readonly draftId: string
    readonly reloadGeneration: number
    readonly response: VersionedResponse<DesignSelectionDraft>
  } | null>(null)
  // What to seed the next `DesignPickerBody` mount's reference-image checkbox with. Tracked apart
  // from `resolvedDraft`, and deliberately outliving it: `resolvedDraft` steps aside as soon as the
  // draft itself catches up, which can land before the picker read (re-keyed to the migrated
  // service type) does — and until that read lands, `DesignPickerBody` has not mounted yet to
  // consume this value at all. Retiring it on the same schedule as `resolvedDraft` would drop it in
  // that gap; it needs to survive until the mount that actually reads it, but no longer than that —
  // `DesignPickerBody` only reads its `initialHasReferenceImage` prop once, at mount, so a later
  // remount (a conflict's own "read it again", or a further migration) must not still be handed this
  // answer instead of the version it is actually enclosing. `version` is the migrated draft's own
  // version, which is also `effectiveDraft.version` for as long as no such later remount has
  // happened — the moment one does, the comparison below stops matching on its own.
  const [carriedHasReferenceImage, setCarriedHasReferenceImage] = useState<{
    readonly draftId: string
    readonly version: string | undefined
    readonly value: boolean
  } | null>(null)
  // Leaving the draft any override was captured for discards them all outright, rather than merely
  // hiding them while elsewhere: returning to that same draft later re-reads it fresh (the read
  // above is keyed by `draftId`), and a still-held override would otherwise outrank that fresh
  // answer — including a `null` one, if another client had since resolved or migrated it.
  const [overridesDraftId, setOverridesDraftId] = useState(draftId)
  if (draftId !== overridesDraftId) {
    setOverridesDraftId(draftId)
    setForcedMigration(null)
    setResolvedDraft(null)
    setCarriedHasReferenceImage(null)
  }
  const activeResolvedDraft =
    resolvedDraft !== null &&
    resolvedDraft.draftId === draftId &&
    draftValueGeneration < resolvedDraft.reloadGeneration
      ? resolvedDraft
      : null
  const effectiveDraft = activeResolvedDraft?.response ?? draft.value
  const carriedHasReferenceImageForDraft =
    carriedHasReferenceImage !== null &&
    carriedHasReferenceImage.draftId === draftId &&
    carriedHasReferenceImage.version === effectiveDraft?.version
      ? carriedHasReferenceImage.value
      : false
  const forcedMigrationForDraft =
    forcedMigration !== null && forcedMigration.draftId === draftId ? forcedMigration : null
  const migrationPrompt =
    effectiveDraft?.value.migrationPrompt ?? forcedMigrationForDraft?.prompt ?? null
  const pendingMigration = effectiveDraft !== null && migrationPrompt !== null
  const pickerServiceTypeId = effectiveDraft?.value.serviceTypeId ?? null
  const picker = useAdminResource(
    `design-picker:${pendingMigration || pickerServiceTypeId === null ? '' : pickerServiceTypeId}`,
    (signal) =>
      pendingMigration || pickerServiceTypeId === null
        ? new Promise<DesignPicker>(() => {
            // Never resolves: there is nothing to read until the migration gate below is resolved,
            // and the key above changes the moment it is, starting a real read then.
          })
        : readCatalogDesignPicker(pickerServiceTypeId, signal),
  )
  // `useAdminResource` deliberately keeps its previous value on screen across a key change, so a
  // migration that re-keys this read to the new service type still shows the retired groups and
  // options until the new GET lands — long enough for Reception to select one and have it rejected
  // against the newly pinned draft. Rendering only once the retained value actually matches the
  // service type it was just re-keyed to closes that window; a mismatch reads as still loading.
  const pickerForDraft =
    picker.value !== null && picker.value.serviceTypeId === pickerServiceTypeId
      ? picker.value
      : null

  const starting = draftId === undefined && startFailure === null

  return (
    <section className="page catalog design-picker">
      <h1>
        <FormattedMessage id="catalog.design.picker.title" />
      </h1>

      {draftId === undefined ? <AuthProblemAlert failure={startFailure} /> : null}

      {!network.online && draftId === undefined ? (
        <OfflineBlockedAction
          action={intl.formatMessage({ id: 'catalog.design.picker.offline.start' })}
        />
      ) : starting || draftId === undefined ? (
        <LoadingState what={intl.formatMessage({ id: 'catalog.design.picker.loading' })} />
      ) : draft.loading ? (
        <LoadingState what={intl.formatMessage({ id: 'catalog.design.picker.loading' })} />
      ) : effectiveDraft === null ? (
        <AuthProblemAlert failure={draft.failure} />
      ) : effectiveDraft.value.consumedAt !== null ? (
        <EmptyState
          iconName="check"
          live="polite"
          title={intl.formatMessage({ id: 'catalog.design.picker.consumed.title' })}
        >
          {intl.formatMessage({ id: 'catalog.design.picker.consumed.body' })}
        </EmptyState>
      ) : migrationPrompt !== null ? (
        <DesignMigrationGate
          draftId={draftId}
          hasReferenceImage={forcedMigrationForDraft?.hasReferenceImage ?? false}
          migrationPrompt={migrationPrompt}
          onMigrated={(migrated) => {
            setResolvedDraft({ draftId, reloadGeneration: reloads + 1, response: migrated })
            setCarriedHasReferenceImage({
              draftId,
              version: migrated.version,
              value: forcedMigrationForDraft?.hasReferenceImage ?? false,
            })
            setForcedMigration(null)
            setReloads((count) => count + 1)
          }}
          version={forcedMigrationForDraft?.version ?? effectiveDraft.version ?? ''}
        />
      ) : (
        <>
          <AuthProblemAlert failure={picker.failure} />
          {pickerForDraft === null ? (
            picker.failure === null ? (
              <LoadingState what={intl.formatMessage({ id: 'catalog.design.picker.loading' })} />
            ) : null
          ) : pickerForDraft.groups.length === 0 ? (
            <EmptyState
              iconName="alert-circle"
              live="polite"
              title={intl.formatMessage({ id: 'catalog.design.picker.empty.title' })}
            >
              {intl.formatMessage({ id: 'catalog.design.picker.empty.body' })}
            </EmptyState>
          ) : (
            <DesignPickerBody
              draftId={draftId}
              // Keyed by the tag the read carried: a reload after a conflict (or a migration)
              // remounts this with the fresh draft, the same reasoning `MeasurementDraftRoute`
              // uses for its wizard.
              key={effectiveDraft.version ?? 'untagged'}
              initial={effectiveDraft}
              initialHasReferenceImage={carriedHasReferenceImageForDraft}
              picker={pickerForDraft}
              onMigrationDetected={(migration) => {
                setForcedMigration({ draftId, ...migration })
              }}
              onReload={() => {
                setReloads((count) => count + 1)
              }}
            />
          )}
        </>
      )}
    </section>
  )
}

interface DesignMigrationGateProps {
  readonly draftId: string
  readonly migrationPrompt: NonNullable<DesignSelectionDraft['migrationPrompt']>
  readonly version: string
  /**
   * Whether a reference image was already answered for before this migration was noticed —
   * `hasReferenceImage` is UI-only state `DesignPickerBody` never persists on the draft itself, so
   * it would otherwise be lost the moment a mid-session republish swaps that screen out for this
   * gate, and migrating with `false` would report an already-satisfied requires-attachment rule as
   * unmet again. `false` for a draft resumed already pinned to a stale version, since nothing has
   * been answered for it yet in this session either way.
   */
  readonly hasReferenceImage: boolean
  /**
   * The draft was migrated. Carries the migrate response's own draft and version rather than
   * asking the route to wait on a fresh `GET`: that read would still show the pre-migration draft
   * until it resolved, and if it were slow or failed, the route would either flash the stale
   * picker (a remounted body autosaving against the old ETag) or get stuck showing the gate.
   */
  readonly onMigrated: (migrated: VersionedResponse<DesignSelectionDraft>) => void
}

/**
 * What stands between a resumed draft and the picker: the catalogue moved on since it was pinned,
 * and Reception chooses to update to the current version or to finish on this one (#142).
 *
 * "Finish on this version" is not a request — there is nothing to migrate away from and nothing to
 * ask the server for — so it is a purely local acknowledgement. What it leaves the screen showing is
 * a plain notice rather than the interactive picker: Reception holds no permission to read a
 * superseded catalogue version's groups and options (that is `catalog.edit`'s screen, not
 * `catalog.design.select`'s), so there is nothing here to render labels, illustrations or rules
 * from. The selections already saved on the draft stand as they are for whoever confirms the garment
 * to read back.
 */
function DesignMigrationGate({
  draftId,
  hasReferenceImage,
  migrationPrompt,
  version,
  onMigrated,
}: DesignMigrationGateProps) {
  const intl = useIntl()
  const [acknowledged, setAcknowledged] = useState(false)
  const [migrating, setMigrating] = useState(false)
  const [migrateFailure, setMigrateFailure] = useState<unknown>(null)
  // A fresh key on every attempt would mean a retry after a lost response — the migration
  // succeeded server-side, but this screen never heard back — asks the server to do it a second
  // time under a key it has never seen, and `version` is now stale from the first attempt's own
  // success, so the retry is refused as a conflict rather than replaying the first outcome. The
  // key changes only when the request it would be sent with actually differs. This component isn't
  // remounted on a client-side navigation between drafts (unlike `DesignPickerBody`, which is keyed
  // by version), so `draftId` is part of the fingerprint too — otherwise two drafts that happen to
  // share a version tag (commonly the first, `W/"1"`) would reuse the first draft's key for the
  // second and the server would reject it as a reused key on a different resolved path.
  const migrateKeyRef = useRef<{ readonly fingerprint: string; readonly key: string } | null>(null)

  const migrate = async (): Promise<void> => {
    setMigrating(true)
    setMigrateFailure(null)
    try {
      const fingerprint = `${draftId}:${version}:${String(hasReferenceImage)}`
      const existing = migrateKeyRef.current
      const key =
        existing !== null && existing.fingerprint === fingerprint
          ? existing.key
          : crypto.randomUUID()
      migrateKeyRef.current = { fingerprint, key }

      const outcome = await migrateCatalogDesignDraft({
        draftId,
        hasReferenceImage,
        version,
        idempotencyKey: key,
      })
      onMigrated({ value: outcome.value.draft, version: outcome.version })
    } catch (cause: unknown) {
      setMigrateFailure(cause)
    } finally {
      setMigrating(false)
    }
  }

  if (acknowledged) {
    return (
      <EmptyState
        actions={
          <Button
            onClick={() => {
              setAcknowledged(false)
            }}
            variant="secondary"
          >
            {intl.formatMessage({ id: 'catalog.design.migration.migrate' })}
          </Button>
        }
        iconName="info"
        live="polite"
        title={intl.formatMessage({ id: 'catalog.design.migration.finished.title' })}
      >
        {intl.formatMessage({ id: 'catalog.design.migration.finished.body' })}
      </EmptyState>
    )
  }

  return (
    <Dialog
      closeOnScrimPress={false}
      description={intl.formatMessage({ id: 'catalog.design.migration.description' })}
      onClose={() => {
        // Finishing on the pinned version is the other sanctioned choice — dismissing says exactly
        // that, and needs no request: it is what happens by simply not migrating.
        setAcknowledged(true)
      }}
      open
      title={intl.formatMessage({ id: 'catalog.design.migration.title' })}
      footer={
        <>
          <Button
            onClick={() => {
              setAcknowledged(true)
            }}
            variant="secondary"
          >
            {intl.formatMessage({ id: 'catalog.design.migration.keep' })}
          </Button>
          <Button
            busy={migrating}
            onClick={() => {
              void migrate()
            }}
            unavailable={!migrationPrompt.serviceTypeStillOffered}
            variant="primary"
          >
            {intl.formatMessage({ id: 'catalog.design.migration.migrate' })}
          </Button>
        </>
      }
    >
      <AuthProblemAlert failure={migrateFailure} />
      <ul>
        {migrationPrompt.changes.map((change) => (
          <li data-drops={dropsAChoice(change.kind) ? 'true' : undefined} key={change.message}>
            {change.message}
          </li>
        ))}
      </ul>
    </Dialog>
  )
}

function mapFromSelections(selections: readonly DesignDraftSelection[]): DesignPickerSelections {
  return new Map(selections.map((selection) => [selection.groupCode, selection.optionCodes]))
}

function payloadFromMap(selections: DesignPickerSelections): readonly DesignDraftSelection[] {
  return [...selections.entries()]
    .filter(([, codes]) => codes.length > 0)
    .map(([groupCode, optionCodes]) => ({ groupCode, optionCodes }))
}

/** Folds what `check` auto-selected into the map, or returns the same map when nothing is new. */
function mergeAutoSelections(
  selections: DesignPickerSelections,
  autoSelections: readonly DesignAutoSelection[],
): DesignPickerSelections {
  let changed = false
  const next = new Map(selections)
  for (const auto of autoSelections) {
    const held = next.get(auto.groupCode) ?? []
    if (!held.includes(auto.optionCode)) {
      next.set(auto.groupCode, [...held, auto.optionCode])
      changed = true
    }
  }
  return changed ? next : selections
}

/** The operand as a sentence fragment, in the customer's words, for why a rule's effect applies. */
function describeOperand(
  intl: IntlShape,
  groups: readonly DesignPickerGroup[],
  operand: CatalogDesignOperand,
): string {
  if (operand.form === 'Always') {
    return intl.formatMessage({ id: 'catalog.design.picker.operand.always' })
  }
  const group = operandGroupName(groups, operand) ?? ''
  if (operand.form === 'AnySelection') {
    return intl.formatMessage({ id: 'catalog.design.picker.operand.anySelection' }, { group })
  }
  const options = new Intl.ListFormat(intl.locale, { type: 'disjunction' }).format(
    operandOptionNames(groups, operand),
  )
  const messageId =
    operand.form === 'NotEquals'
      ? 'catalog.design.picker.operand.notEquals'
      : operand.form === 'In'
        ? 'catalog.design.picker.operand.in'
        : operand.form === 'Includes'
          ? 'catalog.design.picker.operand.includes'
          : operand.form === 'Excludes'
            ? 'catalog.design.picker.operand.excludes'
            : 'catalog.design.picker.operand.equals'
  return intl.formatMessage({ id: messageId }, { group, options })
}

interface DesignPickerBodyProps {
  readonly draftId: string
  readonly initial: VersionedResponse<DesignSelectionDraft>
  /**
   * What to seed the reference-image checkbox with. `hasReferenceImage` is UI-only state the
   * draft itself never carries, so a remount that follows a migration — which folded it into the
   * migrate request — would otherwise reset it to unchecked and reintroduce the very
   * requires-attachment violation that migration had just resolved.
   */
  readonly initialHasReferenceImage: boolean
  readonly picker: DesignPicker
  readonly onReload: () => void
  /** A republish was noticed mid-session, from a read this screen already made. */
  readonly onMigrationDetected: (migration: {
    readonly prompt: DesignMigrationPrompt
    readonly version: string
    readonly hasReferenceImage: boolean
  }) => void
}

function DesignPickerBody({
  draftId,
  initial,
  initialHasReferenceImage,
  picker,
  onReload,
  onMigrationDetected,
}: DesignPickerBodyProps) {
  const intl = useIntl()
  const network = useNetworkState()

  const [selections, setSelections] = useState<DesignPickerSelections>(() =>
    mapFromSelections(initial.value.selections),
  )
  const [instructions, setInstructions] = useState(initial.value.instructions ?? '')
  const [hasReferenceImage, setHasReferenceImage] = useState(initialHasReferenceImage)
  const [check, setCheck] = useState<DesignCheck | null>(null)
  /**
   * The exact inputs `check` was computed for. `check` answers for whatever `commit` submitted at
   * the time, and the instant any of these three moves on, that answer is about a selection set
   * that no longer exists — comparing them at render time, rather than clearing `check` from an
   * effect, is what keeps a failed retry (or the 500ms before the debounce even fires) from reading
   * a stale "clean" for input nobody has verified, without the render-only rule an effect that
   * calls `setState` on every keystroke would otherwise break.
   */
  const [checkedFor, setCheckedFor] = useState<{
    readonly selections: DesignPickerSelections
    readonly instructions: string
    readonly hasReferenceImage: boolean
  } | null>(null)
  const [saving, setSaving] = useState(false)
  const [saveFailure, setSaveFailure] = useState<unknown>(null)
  const [conflict, setConflict] = useState(false)
  /**
   * What brought an option in on the customer's behalf, keyed `groupCode.optionCode` — "the summary
   * lists what A brought with it" (`docs/prd/design-options.md` section 4). Once folded into
   * `selections` an auto-selected option is ordinary state, and the very next `check` that finds it
   * already satisfied stops reporting it as one at all, so this is the only place that fact survives
   * to be shown. It is cleared for an option the moment a person touches that option directly —
   * choosing it again, or away from it, is choosing it, not the rule bringing it along.
   */
  const [autoSelectedBy, setAutoSelectedBy] = useState<ReadonlyMap<string, DesignAutoSelection>>(
    new Map(),
  )
  const [zoom, setZoom] = useState<{
    readonly groupCode: string
    readonly optionCode: string
  } | null>(null)

  const tagRef = useRef(initial.version ?? '')
  const committingRef = useRef(false)
  const queuedRef = useRef(false)
  const saveKeyRef = useRef<{ readonly fingerprint: string; readonly key: string } | null>(null)

  // `commit` reads every mutable input through a ref rather than closing over `selections`,
  // `instructions` and `hasReferenceImage` directly, and stays the same function across renders
  // (its own deps are just `draftId`). A change that arrives while a save is already in flight sets
  // `queuedRef` and this same function replays itself once the in-flight one finishes — with a
  // closure that captured the state at the moment of THAT change would replay the state as it stood
  // when the save started, not what changed while it waited, silently dropping the latest edit.
  const selectionsRef = useRef(selections)
  const instructionsRef = useRef(instructions)
  const hasReferenceImageRef = useRef(hasReferenceImage)
  const onlineRef = useRef(network.online)
  // A ref rather than the `commit` binding itself for the recursive replay below: referencing a
  // `useCallback` result from inside its own body defeats the compiler's memoization analysis
  // (`react-hooks/preserve-manual-memoization`), and a ref updated after every render always calls
  // whichever `commit` currently exists without the function needing to name itself.
  const commitRef = useRef<() => Promise<void>>(() => Promise.resolve())
  useEffect(() => {
    selectionsRef.current = selections
  }, [selections])
  useEffect(() => {
    instructionsRef.current = instructions
  }, [instructions])
  useEffect(() => {
    hasReferenceImageRef.current = hasReferenceImage
  }, [hasReferenceImage])
  useEffect(() => {
    onlineRef.current = network.online
  }, [network.online])

  const commit = useCallback(async (): Promise<void> => {
    if (!onlineRef.current) {
      return
    }
    if (committingRef.current) {
      queuedRef.current = true
      return
    }
    committingRef.current = true
    setSaving(true)
    setSaveFailure(null)

    // Captured once, up front: what this request actually saves and checks. If a later edit
    // arrives before the check answers, `selectionsRef.current` moves on but this does not — and
    // the auto-selections below are folded back only while the two still agree. `check` reads no
    // body of its own; it reports on whatever the save just before it persisted, so an
    // auto-selection computed against `submittedSelections` means nothing once that snapshot is no
    // longer current, and appending it to the newer one could resurrect a choice already changed
    // away from. The queued replay this same change triggers asks again, against what is current.
    const submittedSelections = selectionsRef.current
    const submittedInstructions = instructionsRef.current
    const submittedHasReferenceImage = hasReferenceImageRef.current
    // Set below when this commit's own migration-detection read finds one pending. Reporting it is
    // deferred to the `finally` block: a queued edit waiting behind this commit hasn't reached the
    // server at all yet, and this screen is about to be swapped for the migration gate the moment
    // `onMigrationDetected` fires — unmounting before that edit is ever saved would discard it
    // silently. So when one is queued, its own replay is awaited here instead, and it is the
    // replay's own (later, more current) detection that gets reported, not this one.
    let migrationDetected: {
      readonly prompt: DesignMigrationPrompt
      readonly version: string
      readonly hasReferenceImage: boolean
    } | null = null

    try {
      const body: SaveDesignSelectionsRequest = {
        selections: payloadFromMap(submittedSelections),
        instructions: submittedInstructions.trim() === '' ? null : submittedInstructions,
      }
      const fingerprint = `${tagRef.current}:${JSON.stringify(body)}`
      const existingKey = saveKeyRef.current
      const key =
        existingKey !== null && existingKey.fingerprint === fingerprint
          ? existingKey.key
          : crypto.randomUUID()
      saveKeyRef.current = { fingerprint, key }

      const saved = await saveCatalogDesignDraft({
        draftId,
        body,
        version: tagRef.current,
        idempotencyKey: key,
      })
      tagRef.current = saved.version ?? tagRef.current
      setConflict(false)

      const result = await checkCatalogDesignDraft(draftId, submittedHasReferenceImage)
      setCheck(result)
      setCheckedFor({
        selections: submittedSelections,
        instructions: submittedInstructions,
        hasReferenceImage: submittedHasReferenceImage,
      })

      if (selectionsRef.current === submittedSelections) {
        const newlyAdded = result.autoSelections.filter(
          (auto) => !(submittedSelections.get(auto.groupCode) ?? []).includes(auto.optionCode),
        )
        const merged = mergeAutoSelections(submittedSelections, result.autoSelections)
        if (merged !== submittedSelections) {
          selectionsRef.current = merged
          setSelections(merged)
        }
        if (newlyAdded.length > 0) {
          setAutoSelectedBy((current) => {
            const next = new Map(current)
            for (const auto of newlyAdded) {
              next.set(`${auto.groupCode}.${auto.optionCode}`, auto)
            }
            return next
          })
        }
      }

      // Neither answer above ever carries a fresh migration prompt: the save endpoint always
      // returns one with a null plan, and check answers nothing about it at all. Asking the draft
      // itself is the only way to notice a republish that happened while this screen was already
      // open. This read has already succeeded by the time `fresh` exists, so the prompt it carries
      // goes straight to the route rather than through another (avoidable, and possibly failing)
      // read of its own — along with this read's own version, which the migration gate must send
      // as its precondition: the autosave above already advanced the draft's ETag past whatever
      // `draft.value.version` still holds.
      const fresh = await readCatalogDesignDraft(draftId)
      const freshVersion = fresh.version ?? tagRef.current
      if (freshVersion !== tagRef.current) {
        // Another write landed in the gap between this save and this read — `fresh` answers for
        // selections this screen never rendered, and trusting its version as the migration
        // precondition would let `POST /migrate` satisfy `If-Match` against that unseen state
        // instead of being refused, re-pinning past a change nobody here has looked at. Surfacing
        // it the same way the save above surfaces its own ETag mismatch — a conflict, with a manual
        // re-read — is what actually needed to happen; migration detection waits for that re-read.
        setConflict(true)
      } else if (fresh.value.migrationPrompt !== null) {
        migrationDetected = {
          prompt: fresh.value.migrationPrompt,
          version: freshVersion,
          // The live ref, not `submittedHasReferenceImage`: a change to this checkbox made while
          // this request was in flight has no save of its own pending (see below), so this is its
          // only remaining path to being reflected in what gets reported.
          hasReferenceImage: hasReferenceImageRef.current,
        }
      }
    } catch (cause: unknown) {
      if (cause instanceof ApiError && cause.code === 'catalog.design-draft-changed') {
        setConflict(true)
      } else {
        setSaveFailure(cause)
      }
    } finally {
      setSaving(false)
      committingRef.current = false
      const replay = queuedRef.current
      queuedRef.current = false
      if (replay && migrationDetected !== null) {
        // Awaited, not fired-and-forgotten: the queued edit's own save must land, and its own
        // migration-detection read is what decides what (if anything) gets reported — reporting
        // this commit's own finding first would let the gate render against a version the replay's
        // save is about to advance past, the same conflict dropping the replay outright was meant
        // to avoid, only now by discarding the edit that motivated it instead.
        await commitRef.current()
      } else {
        if (replay) {
          void commitRef.current()
        }
        if (migrationDetected !== null) {
          onMigrationDetected(migrationDetected)
        }
      }
    }
  }, [draftId, onMigrationDetected])

  useEffect(() => {
    commitRef.current = commit
  }, [commit])

  useEffect(() => {
    if (!network.online) {
      return
    }
    const timer = setTimeout(() => {
      void commit()
    }, 500)
    return () => {
      clearTimeout(timer)
    }
  }, [commit, network.online, selections, instructions, hasReferenceImage])

  const currentCheck =
    check !== null &&
    checkedFor !== null &&
    checkedFor.selections === selections &&
    checkedFor.instructions === instructions &&
    checkedFor.hasReferenceImage === hasReferenceImage
      ? check
      : null

  const effects = evaluateDesignPickerEffects(picker.groups, picker.rules, selections)

  const zoomedOption =
    zoom === null
      ? null
      : (picker.groups
          .find((group) => group.code === zoom.groupCode)
          ?.options.find((option) => option.code === zoom.optionCode) ?? null)

  const toggle = (group: DesignPickerGroup, optionCode: string, checked: boolean): void => {
    setSelections((current) => {
      const next = new Map(current)
      if (group.selectionMode === 'SingleChoice') {
        next.set(group.code, checked ? [optionCode] : [])
        return next
      }
      const held = next.get(group.code) ?? []
      next.set(
        group.code,
        checked
          ? [...held.filter((code) => code !== optionCode), optionCode]
          : held.filter((code) => code !== optionCode),
      )
      return next
    })
    // Touching this option directly is choosing it, whichever way — it is no longer a rule's doing.
    setAutoSelectedBy((current) => {
      const key = `${group.code}.${optionCode}`
      if (!current.has(key)) {
        return current
      }
      const next = new Map(current)
      next.delete(key)
      return next
    })
  }

  const violationsFor = (groupCode: string) =>
    (currentCheck?.violations ?? []).filter((violation) => violation.groupCode === groupCode)

  return (
    <>
      {conflict ? (
        <Alert
          actions={
            <Button iconName="refresh" onClick={onReload} variant="secondary">
              {intl.formatMessage({ id: 'catalog.design.picker.conflict.reload' })}
            </Button>
          }
          live="assertive"
          title={intl.formatMessage({ id: 'catalog.design.picker.conflict.title' })}
          tone="warning"
        >
          {intl.formatMessage({ id: 'catalog.design.picker.conflict.body' })}
        </Alert>
      ) : null}

      <AuthProblemAlert failure={saveFailure} />

      {!network.online ? (
        <OfflineBlockedAction
          action={intl.formatMessage({ id: 'catalog.design.picker.offline.save' })}
        >
          {intl.formatMessage({ id: 'states.blocked.inputKept' })}
        </OfflineBlockedAction>
      ) : null}

      <fieldset className="design-picker__groups" disabled={!network.online}>
        <legend className="visually-hidden">
          {intl.formatMessage({ id: 'catalog.design.picker.title' })}
        </legend>

        {[...picker.groups]
          .sort((left, right) => left.displayOrder - right.displayOrder)
          .map((group) => {
            const chosen = selections.get(group.code) ?? []
            const requiredReason = effects.requiredGroups.get(group.code)
            const choicePrompt = violationsFor(group.code).find(
              (violation) => violation.code === 'design.requires-choice',
            )

            return (
              <section
                aria-labelledby={`design-group-${group.code}`}
                className="design-picker__group"
                key={group.code}
              >
                <div className="design-picker__groupHeading">
                  <h2 id={`design-group-${group.code}`}>{group.name}</h2>
                  {group.selectionMode === 'MultipleChoice' ? (
                    <span className="design-picker__groupCount">
                      {intl.formatMessage(
                        { id: 'catalog.design.picker.group.count' },
                        { count: chosen.length },
                      )}
                    </span>
                  ) : null}
                </div>

                {requiredReason === undefined ? null : (
                  <p className="design-picker__requiresBanner" role="status">
                    {intl.formatMessage(
                      { id: 'catalog.design.picker.reason.requires' },
                      { reason: describeOperand(intl, picker.groups, requiredReason.antecedent) },
                    )}
                  </p>
                )}

                {choicePrompt === undefined ? null : (
                  <div className="design-picker__requiresBanner" role="status">
                    <p>{choicePrompt.message}</p>
                    <p>
                      {choicePrompt.optionCodes.map((code) => (
                        <Button
                          key={code}
                          onClick={() => {
                            toggle(group, code, true)
                          }}
                          variant="secondary"
                        >
                          {optionName(picker.groups, group.code, code)}
                        </Button>
                      ))}
                    </p>
                  </div>
                )}

                <div className="design-picker__cards">
                  {[...group.options]
                    .sort((left, right) => left.displayOrder - right.displayOrder)
                    .map((option) => {
                      const excludeReason = effects.disabledOptions.get(
                        `${group.code}.${option.code}`,
                      )
                      const disabledReason = !network.online
                        ? intl.formatMessage({ id: 'catalog.design.picker.offline.disabled' })
                        : excludeReason === undefined
                          ? null
                          : intl.formatMessage(
                              { id: 'catalog.design.picker.reason.excluded' },
                              {
                                reason: describeOperand(
                                  intl,
                                  picker.groups,
                                  excludeReason.antecedent,
                                ),
                              },
                            )

                      return (
                        <DesignOptionCard
                          controlId={`design-option-${group.code}-${option.code}`}
                          disabledReason={chosen.includes(option.code) ? null : disabledReason}
                          key={option.code}
                          kind={group.selectionMode === 'SingleChoice' ? 'radio' : 'checkbox'}
                          name={`design-group-${group.code}`}
                          onToggle={(checked) => {
                            toggle(group, option.code, checked)
                          }}
                          onZoom={() => {
                            setZoom({ groupCode: group.code, optionCode: option.code })
                          }}
                          option={option}
                          selected={chosen.includes(option.code)}
                        />
                      )
                    })}
                </div>
              </section>
            )
          })}
      </fieldset>

      {effects.attachmentReasons.length === 0 ? null : (
        <Alert live="polite" tone="info">
          {effects.attachmentReasons.map((reason) => (
            <p className="state-line" key={reason.ruleIdentifier}>
              {intl.formatMessage(
                { id: 'catalog.design.picker.attachment.reason' },
                { reason: describeOperand(intl, picker.groups, reason.antecedent) },
              )}
            </p>
          ))}
          <Checkbox
            id="design-picker-reference-image"
            label={intl.formatMessage({ id: 'catalog.design.picker.attachment.confirm' })}
            name="hasReferenceImage"
            onValueChange={setHasReferenceImage}
            value={hasReferenceImage}
          />
        </Alert>
      )}

      <TextArea
        description={intl.formatMessage({ id: 'catalog.design.picker.instructions.hint' })}
        disabled={!network.online}
        id="design-picker-instructions"
        label={intl.formatMessage({ id: 'catalog.design.picker.instructions' })}
        name="instructions"
        onValueChange={setInstructions}
        value={instructions}
      />

      <DesignSelectionSummary
        autoSelectedBy={autoSelectedBy}
        check={currentCheck}
        instructions={instructions}
        picker={picker}
        saving={saving}
        selections={selections}
      />

      {zoomedOption === null ? null : (
        <Dialog
          onClose={() => {
            setZoom(null)
          }}
          open
          title={zoomedOption.name}
        >
          <div className="design-picker__zoomFigure">
            <IllustrationPreview
              alt={zoomedOption.illustrationAlt}
              illustrationKey={zoomedOption.illustrationKey}
            />
            <p>{zoomedOption.helpText}</p>
          </div>
        </Dialog>
      )}
    </>
  )
}

interface DesignSelectionSummaryProps {
  readonly picker: DesignPicker
  readonly selections: DesignPickerSelections
  readonly instructions: string
  readonly check: DesignCheck | null
  readonly saving: boolean
  /** What brought each auto-selected option in, keyed `groupCode.optionCode`. */
  readonly autoSelectedBy: ReadonlyMap<string, DesignAutoSelection>
}

/**
 * Every group, chosen or not, the price-list items and day impact the choices carry, and the notes
 * — "what Reception reads back to the customer" (#142).
 *
 * Not `GarmentDesignCard`: that component renders a `GarmentDesignSnapshot`, whose version number and
 * category label come from Orders at confirmation time (#32a) and have no source here — inventing
 * one would be exactly the unsourced number this repository's own rules refuse. This reads the same
 * groups and selections the cards above do, plus whatever `check` last said.
 */
function DesignSelectionSummary({
  picker,
  selections,
  instructions,
  check,
  saving,
  autoSelectedBy,
}: DesignSelectionSummaryProps) {
  const intl = useIntl()

  const rows = [...picker.groups]
    .sort((left, right) => left.displayOrder - right.displayOrder)
    .map((group) => {
      const chosen = selections.get(group.code) ?? []
      const options = chosen
        .map((code) => group.options.find((option) => option.code === code))
        .filter((option): option is (typeof group.options)[number] => option !== undefined)
      const blocked = (check?.violations ?? []).some(
        (violation) => violation.groupCode === group.code && violation.blocks,
      )
      return { group, options, blocked }
    })

  const priceListItems = rows.flatMap(({ options }) =>
    options.flatMap((option) =>
      option.priceListItemCode === null ? [] : [option.priceListItemCode],
    ),
  )
  const totalDays = rows.reduce(
    (total, { options }) => total + options.reduce((sum, option) => sum + option.timeImpactDays, 0),
    0,
  )

  return (
    <section aria-labelledby="design-summary" className="design-picker__summary">
      <h2 id="design-summary">{intl.formatMessage({ id: 'catalog.design.summary.title' })}</h2>

      {check === null ? (
        <p aria-live="polite">
          {intl.formatMessage({
            id: saving ? 'catalog.design.summary.checking' : 'catalog.design.summary.unchecked',
          })}
        </p>
      ) : (
        <p aria-live="polite">
          {intl.formatMessage({
            id: check.confirmable
              ? 'catalog.design.summary.clean'
              : 'catalog.design.summary.blocked',
          })}
        </p>
      )}

      <dl>
        {rows.map(({ group, options, blocked }) => {
          const autoNotes = options
            .map((option) => autoSelectedBy.get(`${group.code}.${option.code}`))
            .filter((auto): auto is DesignAutoSelection => auto !== undefined)
            .map((auto) => {
              const rule = picker.rules.find(
                (candidate) => candidate.identifier === auto.ruleIdentifier,
              )
              return intl.formatMessage(
                { id: 'catalog.design.summary.autoSelected' },
                {
                  reason:
                    rule === undefined
                      ? auto.ruleIdentifier
                      : describeOperand(intl, picker.groups, rule.antecedent),
                },
              )
            })

          return (
            <div key={group.code}>
              <dt>{group.name}</dt>
              <dd data-blocked={blocked ? 'true' : undefined}>
                {options.length > 0
                  ? new Intl.ListFormat(intl.locale, { type: 'conjunction' }).format(
                      options.map((option) => option.name),
                    )
                  : blocked
                    ? intl.formatMessage({ id: 'catalog.design.summary.requiredUnset' })
                    : intl.formatMessage({ id: 'catalog.design.summary.notChosen' })}
              </dd>
              {autoNotes.map((note) => (
                <p className="design-picker__summary-autoNote" key={note}>
                  {note}
                </p>
              ))}
            </div>
          )
        })}
      </dl>

      <p>
        {intl.formatMessage(
          { id: 'catalog.design.summary.priceItems' },
          {
            items:
              priceListItems.length === 0
                ? intl.formatMessage({ id: 'catalog.design.summary.priceItems.none' })
                : new Intl.ListFormat(intl.locale, { type: 'conjunction' }).format(priceListItems),
          },
        )}
      </p>
      <p>{intl.formatMessage({ id: 'catalog.design.summary.days' }, { count: totalDays })}</p>

      {instructions.trim() === '' ? null : (
        <p>
          <strong>{intl.formatMessage({ id: 'catalog.design.card.instructions' })}</strong>{' '}
          {instructions}
        </p>
      )}

      {check !== null && check.notes.length > 0 ? (
        <ul aria-label={intl.formatMessage({ id: 'catalog.design.summary.notes' })}>
          {check.notes.map((note) => (
            <li key={note.ruleIdentifier}>{note.text}</li>
          ))}
        </ul>
      ) : null}

      {check !== null && check.violations.some((violation) => violation.blocks) ? (
        <ul aria-label={intl.formatMessage({ id: 'catalog.design.summary.violations' })}>
          {check.violations
            .filter((violation) => violation.blocks)
            .map((violation) => (
              <li
                key={`${violation.code}-${violation.groupCode ?? ''}-${violation.ruleIdentifier ?? ''}`}
              >
                {violation.message}
              </li>
            ))}
        </ul>
      ) : null}
    </section>
  )
}
