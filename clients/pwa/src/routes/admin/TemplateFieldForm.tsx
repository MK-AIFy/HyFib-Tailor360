import { useId, useState } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Button } from '../../components/primitives/Button'
import { Alert } from '../../components/primitives/Alert'
import { Checkbox } from '../../design-system/components/forms/Checkbox'
import { FormErrorSummary } from '../../design-system/components/forms/FormErrorSummary'
import { Select } from '../../design-system/components/forms/Select'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import type { FieldErrorEntry } from '../../design-system/foundations/FieldProps'
import {
  CANONICAL_UNITS,
  INCH_FRACTIONS,
  LIMITS,
  MAXIMUM_CENTIMETRE_DECIMALS,
  BAND_KEYS,
  hasPrecision,
  isChoice,
  validateField,
} from '../../admin/templateFieldForm'
import type {
  BandKey,
  CanonicalUnit,
  FieldFormError,
  FieldFormState,
} from '../../admin/templateFieldForm'
import { TemplateFieldBands } from './TemplateFieldBands'
import { TemplateRuleBuilder } from './TemplateRuleBuilder'

import { unitForBands } from '../../design-system/components/forms/measurementRange'
import type { MeasurementDisplayUnit } from '../../design-system/components/forms/measurement'
import type { TemplateField } from '../../admin/types'
import type { MessageKey } from '../../i18n/en-IN'

/**
 * One field of a draft version, as a form.
 *
 * ## Why a unit changes which controls exist, rather than disabling them
 *
 * The client guide asks for controls that are honest about what the system can do. A `None` field is
 * a choice: it has no bands, no inch step and no decimal places, and it never will — the server
 * nulls the bands out itself and refuses a count that carries a precision. A disabled box says "not
 * now"; nothing at all says "never", which is the truth. So the precision controls are absent for a
 * count and a choice, the options list is absent for anything else, and a sentence says why in each
 * case rather than leaving a person to infer it from a gap.
 *
 * ## Why the key is read-only on an existing field
 *
 * The server ignores a key sent with a `PUT` (`TemplateVersion.cs:276`), because renaming through an
 * edit would orphan every value already filed under the old one. An editable box would therefore
 * appear to work and change nothing, which is worse than no box. The documented rename is a removal
 * and an addition — two audited acts, and the field starts a new history — and the hint says exactly
 * that rather than leaving somebody to discover it.
 *
 * ## Why the summary does not take focus
 *
 * The form revalidates as the person types, and `FormErrorSummary` with `autoFocus` would pull focus
 * out of the control they are still working in. The polite region announces instead — one channel,
 * not both.
 */
export interface TemplateFieldFormProps {
  readonly form: FieldFormState
  /** The field being replaced, or null when this is a new one. Decides the key control. */
  readonly existing: TemplateField | null
  /** Every other key in the version, so a duplicate is refused before the round trip. */
  readonly otherKeys: readonly string[]
  readonly busy: boolean
  /** The version's default display unit, which decides which unit the bands open in. */
  readonly defaultDisplayUnit: string
  readonly submissionId: number
  readonly onChange: (form: FieldFormState) => void
  readonly onSubmit: (form: FieldFormState) => void
  readonly onCancel: () => void
}

export function TemplateFieldForm(props: TemplateFieldFormProps) {
  const {
    form,
    existing,
    otherKeys,
    busy,
    defaultDisplayUnit,
    submissionId,
    onChange,
    onSubmit,
    onCancel,
  } = props
  const intl = useIntl()
  const prefix = useId()

  const isNew = existing === null
  const errors = validateField(form, otherKeys, isNew)
  const shown = submissionId > 0 ? errors : []
  const controlId = (name: string): string => `${prefix}-${name}`

  const messageFor = (error: FieldFormError): string =>
    intl.formatMessage({ id: error.messageId }, error.values)

  const firstFor = (
    field: FieldFormState extends never ? never : keyof FieldFormState,
  ): string | undefined => {
    const found = shown.find((error) => error.field === field)
    return found === undefined ? undefined : messageFor(found)
  }

  const optionError = (index: number): string | undefined => {
    const found = shown.find((error) => error.field === 'options' && error.optionIndex === index)
    return found === undefined ? undefined : messageFor(found)
  }

  const summary: readonly FieldErrorEntry[] = shown.map((error) => ({
    name: error.optionIndex === undefined ? error.field : `options[${error.optionIndex}]`,
    message: messageFor(error),
    controlId:
      error.optionIndex === undefined
        ? controlId(error.field)
        : controlId(`option-${error.optionIndex}-code`),
  }))

  /** An absent refusal is an absent prop: `exactOptionalPropertyTypes` refuses an explicit undefined. */
  const errorProp = (message: string | undefined): { error?: string } =>
    message === undefined ? {} : { error: message }

  const set = <TKey extends keyof FieldFormState>(key: TKey, value: FieldFormState[TKey]): void => {
    onChange({ ...form, [key]: value })
  }

  /**
   * Which units the bands may be read in, and which one is on screen.
   *
   * A field declares a precision per unit, and a unit it has no precision for cannot show a bound at
   * that precision — so the choice is exactly what the field supports. When it supports both, the
   * version's own default decides which opens, so a reviewer checking a bound against a tape does
   * not have to convert it in their head first. The choice is presentation only: the four numbers
   * are held and sent in millimetres either way.
   */
  const availableUnits: readonly MeasurementDisplayUnit[] = [
    ...(form.inchFraction > 0 ? (['in'] as const) : []),
    ...(form.centimetreDecimals > 0 ? (['cm'] as const) : []),
  ]

  const [chosenUnit, setChosenUnit] = useState<MeasurementDisplayUnit | null>(null)
  const bandUnit =
    chosenUnit !== null && availableUnits.includes(chosenUnit)
      ? chosenUnit
      : (unitForBands(form, defaultDisplayUnit) ?? 'in')

  const setBandUnit = (next: MeasurementDisplayUnit): void => {
    setChosenUnit(next)
  }

  /**
   * The rule's refusals, back in the shape the builder reads.
   *
   * `validateField` flattens every refusal into one list keyed by control, and a clause index rides
   * along in the same member an option index uses — so it is unpacked here rather than the builder
   * being taught the flattened shape.
   */
  const ruleErrors = shown
    .filter((error) => error.field === 'rule')
    .map((error) => ({
      messageId: error.messageId,
      ...(error.values === undefined ? {} : { values: error.values }),
      ...(error.optionIndex === undefined ? {} : { clause: error.optionIndex }),
    }))

  const bandErrors: Partial<Record<BandKey, string>> = {}
  for (const error of shown) {
    if (
      BAND_KEYS.includes(error.field as BandKey) &&
      bandErrors[error.field as BandKey] === undefined
    ) {
      bandErrors[error.field as BandKey] = messageFor(error)
    }
  }

  return (
    <form
      aria-label={
        isNew
          ? intl.formatMessage({ id: 'admin.field.addTitle' })
          : intl.formatMessage({ id: 'admin.field.editTitle' }, { label: existing.label })
      }
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit(form)
      }}
    >
      <h3>
        {isNew
          ? intl.formatMessage({ id: 'admin.field.addTitle' })
          : intl.formatMessage({ id: 'admin.field.editTitle' }, { label: existing.label })}
      </h3>

      {summary.length === 0 ? null : (
        <FormErrorSummary autoFocus={false} errors={summary} submissionId={submissionId} />
      )}

      {isNew ? (
        <TextField
          description={intl.formatMessage({ id: 'admin.field.key.hint' })}
          {...errorProp(firstFor('key'))}
          id={controlId('key')}
          name={'key'}
          label={intl.formatMessage({ id: 'admin.field.key' })}
          maxLength={LIMITS.key}
          onValueChange={(next) => {
            set('key', next)
          }}
          required
          value={form.key}
        />
      ) : (
        <TextField
          description={intl.formatMessage({ id: 'admin.field.key.fixedHint' })}
          id={controlId('key')}
          name={'key'}
          label={intl.formatMessage({ id: 'admin.field.key' })}
          readOnly
          value={form.key}
        />
      )}

      <TextField
        description={intl.formatMessage({ id: 'admin.field.label.hint' })}
        {...errorProp(firstFor('label'))}
        id={controlId('label')}
        name={'label'}
        label={intl.formatMessage({ id: 'admin.field.label' })}
        maxLength={LIMITS.label}
        onValueChange={(next) => {
          set('label', next)
        }}
        required
        value={form.label}
      />

      <TextField
        description={intl.formatMessage({ id: 'admin.field.labelTamil.hint' })}
        id={controlId('labelTamil')}
        name={'labelTamil'}
        label={intl.formatMessage({ id: 'admin.field.labelTamil' })}
        maxLength={LIMITS.label}
        onValueChange={(next) => {
          set('labelTamil', next)
        }}
        value={form.labelTamil}
      />

      <TextField
        description={intl.formatMessage({ id: 'admin.field.group.hint' })}
        {...errorProp(firstFor('groupName'))}
        id={controlId('groupName')}
        name={'groupName'}
        label={intl.formatMessage({ id: 'admin.field.group' })}
        maxLength={LIMITS.group}
        onValueChange={(next) => {
          set('groupName', next)
        }}
        required
        value={form.groupName}
      />

      <TextArea
        description={intl.formatMessage({ id: 'admin.field.help.hint' })}
        {...errorProp(firstFor('helpText'))}
        id={controlId('helpText')}
        name={'helpText'}
        label={intl.formatMessage({ id: 'admin.field.help' })}
        maxLength={LIMITS.helpText}
        onValueChange={(next) => {
          set('helpText', next)
        }}
        required
        value={form.helpText}
      />

      <Select
        description={intl.formatMessage({ id: 'admin.field.unit.hint' })}
        emptyLabel={null}
        id={controlId('canonicalUnit')}
        name={'canonicalUnit'}
        label={intl.formatMessage({ id: 'admin.field.unit' })}
        onValueChange={(next) => {
          set('canonicalUnit', next as CanonicalUnit)
        }}
        options={CANONICAL_UNITS.map((unit) => ({
          value: unit,
          label: intl.formatMessage({ id: `admin.field.unit.${unit}` as MessageKey }),
        }))}
        required
        value={form.canonicalUnit}
      />

      {hasPrecision(form.canonicalUnit) ? (
        <>
          <Select
            description={intl.formatMessage({ id: 'admin.field.inchFraction.hint' })}
            emptyLabel={null}
            {...errorProp(firstFor('inchFraction'))}
            id={controlId('inchFraction')}
            name={'inchFraction'}
            label={intl.formatMessage({ id: 'admin.field.inchFraction' })}
            onValueChange={(next) => {
              set('inchFraction', Number(next))
            }}
            options={[
              {
                value: '0',
                label: intl.formatMessage({ id: 'admin.field.inchFraction.none' }),
              },
              ...INCH_FRACTIONS.map((denominator) => ({
                value: String(denominator),
                label: intl.formatMessage(
                  { id: 'admin.field.inchFraction.value' },
                  { denominator },
                ),
              })),
            ]}
            required
            value={String(form.inchFraction)}
          />

          <Select
            emptyLabel={null}
            id={controlId('centimetreDecimals')}
            name={'centimetreDecimals'}
            label={intl.formatMessage({ id: 'admin.field.centimetreDecimals' })}
            onValueChange={(next) => {
              set('centimetreDecimals', Number(next))
            }}
            options={[
              {
                value: '0',
                label: intl.formatMessage({ id: 'admin.field.centimetreDecimals.none' }),
              },
              ...Array.from({ length: MAXIMUM_CENTIMETRE_DECIMALS }, (_unused, index) => ({
                value: String(index + 1),
                label: intl.formatMessage(
                  { id: 'admin.field.centimetreDecimals.value' },
                  { places: index + 1 },
                ),
              })),
            ]}
            required
            value={String(form.centimetreDecimals)}
          />
        </>
      ) : form.canonicalUnit === 'Count' ? (
        <Alert tone="info" live="off">
          <FormattedMessage id="admin.field.wholeNumber" />
        </Alert>
      ) : (
        <Alert tone="info" live="off">
          <FormattedMessage id="admin.field.choiceShape" />
        </Alert>
      )}

      {isChoice(form.canonicalUnit) ? null : (
        <TemplateFieldBands
          available={availableUnits}
          controlId={controlId}
          errors={bandErrors}
          form={form}
          onChange={onChange}
          onUnitChange={setBandUnit}
          unit={bandUnit}
        />
      )}

      {isChoice(form.canonicalUnit) ? (
        <fieldset>
          <legend>
            <FormattedMessage id="admin.field.options" />
          </legend>

          {form.options.length === 0 ? (
            <p>
              <FormattedMessage id="admin.field.options.empty" />
            </p>
          ) : null}

          {form.options.map((option, index) => (
            <div key={`option-${String(index)}`}>
              <TextField
                description={intl.formatMessage({ id: 'admin.field.option.code.hint' })}
                {...errorProp(optionError(index))}
                id={controlId(`option-${String(index)}-code`)}
                name={`option-${String(index)}-code`}
                label={intl.formatMessage({ id: 'admin.field.option.code' })}
                maxLength={LIMITS.optionCode}
                onValueChange={(next) => {
                  // Upper-cased as it is typed rather than on the way out. The stored value is what
                  // appears in an export and in every integration, so a person who types `round`
                  // has to see that `ROUND` is what they are creating — normalising silently would
                  // make the box disagree with the record it produces.
                  set(
                    'options',
                    form.options.map((current, at) =>
                      at === index ? { ...current, code: next.toUpperCase() } : current,
                    ),
                  )
                }}
                required
                value={option.code}
              />
              <TextField
                id={controlId(`option-${String(index)}-label`)}
                name={`option-${String(index)}-label`}
                label={intl.formatMessage({ id: 'admin.field.option.label' })}
                maxLength={LIMITS.label}
                onValueChange={(next) => {
                  set(
                    'options',
                    form.options.map((current, at) =>
                      at === index ? { ...current, label: next } : current,
                    ),
                  )
                }}
                required
                value={option.label}
              />
              <TextField
                id={controlId(`option-${String(index)}-labelTamil`)}
                name={`option-${String(index)}-labelTamil`}
                label={intl.formatMessage({ id: 'admin.field.option.labelTamil' })}
                maxLength={LIMITS.label}
                onValueChange={(next) => {
                  set(
                    'options',
                    form.options.map((current, at) =>
                      at === index ? { ...current, labelTamil: next } : current,
                    ),
                  )
                }}
                value={option.labelTamil}
              />
              <Button
                variant="secondary"
                onClick={() => {
                  set(
                    'options',
                    form.options.filter((_unused, at) => at !== index),
                  )
                }}
                type="button"
              >
                {intl.formatMessage({ id: 'admin.field.options.remove' }, { position: index + 1 })}
              </Button>
            </div>
          ))}

          <Button
            variant="secondary"
            onClick={() => {
              set('options', [...form.options, { code: '', label: '', labelTamil: '' }])
            }}
            type="button"
          >
            <FormattedMessage id="admin.field.options.add" />
          </Button>
        </fieldset>
      ) : null}

      <TemplateRuleBuilder
        controlId={controlId}
        draft={form.rule}
        errors={ruleErrors}
        fieldKeys={otherKeys}
        onChange={(rule) => {
          set('rule', rule)
        }}
        sentence={existing?.rule ?? null}
      />

      <Checkbox
        id={controlId('isRequired')}
        name={'isRequired'}
        label={intl.formatMessage({ id: 'admin.field.required' })}
        onValueChange={(next) => {
          set('isRequired', next)
        }}
        value={form.isRequired}
      />

      <TextField
        description={intl.formatMessage({ id: 'admin.field.diagramKey.hint' })}
        {...errorProp(firstFor('diagramKey'))}
        id={controlId('diagramKey')}
        name={'diagramKey'}
        label={intl.formatMessage({ id: 'admin.field.diagramKey' })}
        maxLength={LIMITS.diagramKey}
        onValueChange={(next) => {
          set('diagramKey', next)
        }}
        value={form.diagramKey}
      />

      {form.diagramKey.trim() === '' ? null : (
        <TextArea
          description={intl.formatMessage({ id: 'admin.field.diagramAlt.hint' })}
          {...errorProp(firstFor('diagramAlt'))}
          id={controlId('diagramAlt')}
          name={'diagramAlt'}
          label={intl.formatMessage({ id: 'admin.field.diagramAlt' })}
          maxLength={LIMITS.diagramAlt}
          onValueChange={(next) => {
            set('diagramAlt', next)
          }}
          required
          value={form.diagramAlt}
        />
      )}

      <Button busy={busy} type="submit" variant="primary">
        <FormattedMessage id="admin.field.save" />
      </Button>
      <Button onClick={onCancel} type="button" variant="secondary">
        <FormattedMessage id="admin.cancel" />
      </Button>
    </form>
  )
}
