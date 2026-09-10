import { useState } from 'react'
import { useIntl } from 'react-intl'
import { Alert } from '../../components/primitives/Alert'
import { MeasurementField } from '../../design-system/components/forms/MeasurementField'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { Select } from '../../design-system/components/forms/Select'
import { unitForBands } from '../../design-system/components/forms/measurementRange'
import type { CentimetreDecimals, InchFractionStep } from '../../i18n/units'
import { evaluateRule, ruleToDraft } from '../../admin/templateRule'
import { groupFields } from '../../admin/templateFieldOrder'
import type { TemplateField, TemplateVersion } from '../../admin/types'

/**
 * The version as a tailor will meet it, and a place to try a value against its range.
 *
 * ## Why a preview and a test-data check are one screen
 *
 * #27 has an acceptance criterion the backend supports and no screen exercises: enter a value
 * against a field and see whether it is inside the bands, needs an acknowledgement or is refused.
 * That is not a separate feature from previewing the wizard — it *is* the preview, once the preview
 * uses the real capture control rather than a picture of one. `MeasurementField` already enforces
 * the hard bounds, offers the confirmation band's acknowledgement and names the expected range in
 * the unit on screen, so rendering the version through it answers both at once and cannot drift
 * from what the wizard will actually do.
 *
 * **Nothing typed here is saved.** There is no draft, no request and no key: the values live in
 * component state for as long as the screen is open, which is what makes it safe to try a number
 * that would be refused.
 *
 * ## Why a hidden field is stated rather than omitted
 *
 * A reviewer checking a rule needs to see that it hid the field, and a field that simply is not
 * there looks identical to a field somebody forgot to add. So a hidden field says it is not asked
 * for with these answers, and a field whose rule cannot be settled says *that*, in the words that
 * explain why it is shown anyway: design choices only exist inside an order, and hiding on missing
 * information drops a measurement the tailor needs.
 */
export interface TemplateCapturePreviewProps {
  readonly version: TemplateVersion
}

export function TemplateCapturePreview(props: TemplateCapturePreviewProps) {
  const { version } = props
  const intl = useIntl()

  /** What has been typed, by field key. Never sent anywhere. */
  const [values, setValues] = useState<Readonly<Record<string, string>>>({})
  const [acknowledged, setAcknowledged] = useState<Readonly<Record<string, boolean>>>({})

  const fields = version.fields ?? []
  const groups = groupFields(fields)

  if (fields.length === 0) {
    return (
      <section>
        <h3>{intl.formatMessage({ id: 'admin.version.preview' })}</h3>
        <p>{intl.formatMessage({ id: 'admin.version.preview.empty' })}</p>
      </section>
    )
  }

  const numeric = (field: TemplateField): number | undefined => {
    const raw = values[field.key]
    if (raw === undefined || raw === '') {
      return undefined
    }
    const parsed = Number(raw)
    return Number.isNaN(parsed) ? undefined : parsed
  }

  /** An untyped field is an absent prop, not an undefined one (`exactOptionalPropertyTypes`). */
  const valueProp = (field: TemplateField): { value?: number } => {
    const parsed = numeric(field)
    return parsed === undefined ? {} : { value: parsed }
  }

  const setValue = (key: string, value: string): void => {
    setValues((all) => ({ ...all, [key]: value }))
  }

  return (
    <section>
      <h3>{intl.formatMessage({ id: 'admin.version.preview' })}</h3>
      <p>{intl.formatMessage({ id: 'admin.version.preview.hint' })}</p>

      {groups.map((group) => (
        <section key={group.name}>
          {/*
            The step's own name, not the editor's "Step: …" heading. A tailor meets a wizard step
            headed by what it is, and repeating the editor's wording would put two headings of the
            same name on one page — which is a person scanning headings landing on the wrong one.
          */}
          <h4>{group.name}</h4>

          {group.fields.map((field) => {
            // Design selections are empty: a preview on this screen is outside an order, which is
            // exactly the case a reviewer needs to see.
            const verdict = evaluateRule(ruleToDraft(field.ruleDefinition), values, {})

            if (!verdict.isShown) {
              return (
                <p key={field.templateFieldId}>
                  {intl.formatMessage(
                    { id: 'admin.version.preview.hidden' },
                    { label: field.label },
                  )}
                </p>
              )
            }

            const unit = unitForBands(
              {
                inchFraction: Number(field.inchFraction),
                centimetreDecimals: Number(field.centimetreDecimals),
              },
              version.defaultDisplayUnit,
            )

            return (
              <div key={field.templateFieldId}>
                {verdict.decided ? null : (
                  <Alert live="polite" tone="info">
                    {intl.formatMessage(
                      { id: 'admin.version.preview.undecidable' },
                      { label: field.label },
                    )}
                  </Alert>
                )}

                {field.canonicalUnit === 'None' ? (
                  <Select
                    description={field.helpText}
                    id={`preview-${field.templateFieldId}`}
                    label={field.label}
                    name={field.key}
                    onValueChange={(next) => {
                      setValue(field.key, next)
                    }}
                    options={field.options.map((option) => ({
                      value: option.code,
                      label: option.label,
                    }))}
                    required={field.isRequired}
                    value={values[field.key] ?? ''}
                  />
                ) : field.canonicalUnit === 'Count' || unit === undefined ? (
                  <NumericStepper
                    description={field.helpText}
                    id={`preview-${field.templateFieldId}`}
                    label={field.label}
                    name={field.key}
                    onValueChange={(next) => {
                      setValue(field.key, String(next))
                    }}
                    required={field.isRequired}
                    {...valueProp(field)}
                  />
                ) : (
                  <MeasurementField
                    acknowledged={acknowledged[field.key] ?? false}
                    bounds={{
                      minimumMillimetres: Number(field.minimumMillimetres),
                      maximumMillimetres: Number(field.maximumMillimetres),
                      ...(field.warnBelowMillimetres === null
                        ? {}
                        : { warnBelowMillimetres: Number(field.warnBelowMillimetres) }),
                      ...(field.warnAboveMillimetres === null
                        ? {}
                        : { warnAboveMillimetres: Number(field.warnAboveMillimetres) }),
                    }}
                    centimetreDecimals={
                      (Number(field.centimetreDecimals) > 0
                        ? Number(field.centimetreDecimals)
                        : 1) as CentimetreDecimals
                    }
                    description={field.helpText}
                    displayUnit={unit}
                    fractionStep={
                      (Number(field.inchFraction) > 0
                        ? Number(field.inchFraction)
                        : 8) as InchFractionStep
                    }
                    id={`preview-${field.templateFieldId}`}
                    label={field.label}
                    name={field.key}
                    onAcknowledgedChange={(next) => {
                      setAcknowledged((all) => ({ ...all, [field.key]: next }))
                    }}
                    onValueChange={(next) => {
                      setValue(field.key, String(next))
                    }}
                    required={field.isRequired}
                    {...valueProp(field)}
                  />
                )}
              </div>
            )
          })}
        </section>
      ))}
    </section>
  )
}
