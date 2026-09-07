import { describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { UserEvent } from '@testing-library/user-event'
import type { ReactElement } from 'react'
import { renderWithProviders } from '../../testing/renderWithProviders'
import { expectNoAccessibilityViolations } from '../../testing/axe'
import { PSEUDO_LOCALE } from '../../../i18n/pseudo'
import type { FieldAriaSource } from '../../foundations/FieldProps'
import { Checkbox } from './Checkbox'
import { DateField } from './DateField'
import { FractionInput } from './FractionInput'
import { MeasurementField } from './MeasurementField'
import { NumericStepper } from './NumericStepper'
import { RadioGroup } from './RadioGroup'
import { Select } from './Select'
import { Switch } from './Switch'
import { TextArea } from './TextArea'
import { TextField } from './TextField'

/**
 * The form contract test.
 *
 * docs/nfr/accessibility-localisation.md section 8.1 says "a screen cannot accidentally ship a
 * field without a label, because the component has nowhere to put the text". That is a claim about
 * every control in the system, so it is asserted against every control in the system, from one
 * table, rather than being re-proved by hand in ten component tests that would each drift.
 *
 * Adding a control to the family means adding a row here. A row that cannot be written — because
 * the control has no visible label, or wires its error somewhere of its own — is the review
 * conversation this file exists to force.
 */

const LABEL = 'Waist'
const NAME = 'waist'

/** The contract members a case may be rendered with. Every control accepts all of them. */
type ContractProps = Partial<
  Pick<
    FieldAriaSource,
    | 'description'
    | 'error'
    | 'warning'
    | 'required'
    | 'disabled'
    | 'readOnly'
    | 'unit'
    | 'describedByIds'
  >
> & { readonly id?: string }

interface ControlCase {
  readonly name: string
  render(props: ContractProps): ReactElement
  /** The element that must carry the id and the aria wiring. */
  host(): HTMLElement
  /** The element that must carry `aria-describedby`. Defaults to the host. */
  describedByHost?(): HTMLElement
  /** Proves the visible label reaches the accessible name (3.3.2 and 2.5.3). */
  assertAccessibleName(): void
  /** False for the controls that own their unit rather than taking one. */
  readonly acceptsUnit: boolean
  /** False where `readOnly` is honoured as `aria-readonly`, the native attribute not existing. */
  readonly nativeReadOnly: boolean
  interact(user: UserEvent): Promise<void>
  readonly expectedValue: unknown
}

const SELECT_OPTIONS = [
  { value: 'ELASTIC', label: 'Elastic' },
  { value: 'DRAWSTRING', label: 'Drawstring' },
]

function makeCase(
  onValueChange: (value: never) => void,
): (name: string) => ControlCase | undefined {
  const change = onValueChange as (value: unknown) => void

  const cases: readonly ControlCase[] = [
    {
      name: 'TextField',
      render: (props) => <TextField label={LABEL} name={NAME} onValueChange={change} {...props} />,
      host: () => screen.getByLabelText(LABEL),
      assertAccessibleName: () => {
        expect(screen.getByRole('textbox', { name: LABEL })).toBeInTheDocument()
      },
      acceptsUnit: true,
      nativeReadOnly: true,
      interact: async (user) => {
        await user.type(screen.getByLabelText(LABEL), '9')
      },
      expectedValue: '9',
    },
    {
      name: 'TextArea',
      render: (props) => <TextArea label={LABEL} name={NAME} onValueChange={change} {...props} />,
      host: () => screen.getByLabelText(LABEL),
      assertAccessibleName: () => {
        expect(screen.getByRole('textbox', { name: LABEL })).toBeInTheDocument()
      },
      acceptsUnit: true,
      nativeReadOnly: true,
      interact: async (user) => {
        await user.type(screen.getByLabelText(LABEL), 'x')
      },
      expectedValue: 'x',
    },
    {
      name: 'Select',
      render: (props) => (
        <Select
          label={LABEL}
          name={NAME}
          onValueChange={change}
          options={SELECT_OPTIONS}
          {...props}
        />
      ),
      host: () => screen.getByLabelText(LABEL),
      assertAccessibleName: () => {
        expect(screen.getByRole('combobox', { name: LABEL })).toBeInTheDocument()
      },
      acceptsUnit: true,
      nativeReadOnly: false,
      interact: async (user) => {
        await user.selectOptions(screen.getByLabelText(LABEL), 'DRAWSTRING')
      },
      expectedValue: 'DRAWSTRING',
    },
    {
      name: 'Checkbox',
      render: (props) => <Checkbox label={LABEL} name={NAME} onValueChange={change} {...props} />,
      host: () => screen.getByRole('checkbox', { name: LABEL }),
      assertAccessibleName: () => {
        // Exact: the visible "Required" marker beside the label is aria-hidden, so it must not
        // reach the accessible name — a voice-control user saying "tap Waist" has to be heard.
        expect(screen.getByRole('checkbox', { name: LABEL })).toBeInTheDocument()
      },
      acceptsUnit: false,
      nativeReadOnly: false,
      interact: async (user) => {
        await user.click(screen.getByRole('checkbox', { name: LABEL }))
      },
      expectedValue: true,
    },
    {
      name: 'Switch',
      render: (props) => <Switch label={LABEL} name={NAME} onValueChange={change} {...props} />,
      host: () => screen.getByRole('switch', { name: LABEL }),
      assertAccessibleName: () => {
        expect(screen.getByRole('switch', { name: LABEL })).toBeInTheDocument()
      },
      acceptsUnit: false,
      nativeReadOnly: false,
      interact: async (user) => {
        await user.click(screen.getByRole('switch', { name: LABEL }))
      },
      expectedValue: true,
    },
    {
      name: 'RadioGroup',
      render: (props) => (
        <RadioGroup
          label={LABEL}
          name={NAME}
          onValueChange={change}
          options={SELECT_OPTIONS}
          {...props}
        />
      ),
      host: () => screen.getByRole('radio', { name: 'Elastic' }),
      describedByHost: () => screen.getByRole('group', { name: new RegExp(LABEL) }),
      assertAccessibleName: () => {
        // The group name is the legend; each option names itself.
        expect(screen.getByRole('group', { name: new RegExp(LABEL) })).toBeInTheDocument()
      },
      acceptsUnit: false,
      nativeReadOnly: false,
      interact: async (user) => {
        await user.click(screen.getByRole('radio', { name: 'Drawstring' }))
      },
      expectedValue: 'DRAWSTRING',
    },
    {
      name: 'DateField',
      render: (props) => (
        <DateField
          label={LABEL}
          name={NAME}
          onValueChange={change}
          showFormatHint={false}
          {...props}
        />
      ),
      host: () => screen.getByLabelText(LABEL),
      assertAccessibleName: () => {
        expect(screen.getByLabelText(LABEL)).toBeInTheDocument()
      },
      acceptsUnit: false,
      nativeReadOnly: true,
      interact: async (user) => {
        await user.type(screen.getByLabelText(LABEL), '2026-09-04')
      },
      expectedValue: '2026-09-04',
    },
    {
      name: 'NumericStepper',
      render: (props) => (
        <NumericStepper label={LABEL} name={NAME} onValueChange={change} value={4} {...props} />
      ),
      host: () => screen.getByLabelText(LABEL),
      assertAccessibleName: () => {
        expect(screen.getByRole('textbox', { name: LABEL })).toBeInTheDocument()
      },
      acceptsUnit: true,
      nativeReadOnly: true,
      interact: async (user) => {
        await user.click(screen.getByRole('button', { name: `Increase ${LABEL}` }))
      },
      expectedValue: 5,
    },
    {
      name: 'FractionInput',
      render: (props) => (
        <FractionInput label={LABEL} name={NAME} onValueChange={change} value={0} {...props} />
      ),
      host: () => screen.getByLabelText(`${LABEL} — whole inches`),
      assertAccessibleName: () => {
        expect(screen.getByRole('group', { name: new RegExp(LABEL) })).toBeInTheDocument()
        expect(screen.getByRole('radiogroup', { name: new RegExp(LABEL) })).toBeInTheDocument()
      },
      acceptsUnit: false,
      nativeReadOnly: true,
      interact: async (user) => {
        await user.click(screen.getByRole('radio', { name: '1/2' }))
      },
      expectedValue: 12.7,
    },
    {
      name: 'MeasurementField',
      render: (props) => (
        <MeasurementField
          displayUnit="in"
          label={LABEL}
          name={NAME}
          onValueChange={change}
          value={0}
          {...props}
        />
      ),
      host: () => screen.getByLabelText(`${LABEL} — whole inches`),
      assertAccessibleName: () => {
        expect(screen.getByRole('group', { name: new RegExp(LABEL) })).toBeInTheDocument()
      },
      acceptsUnit: false,
      nativeReadOnly: true,
      interact: async (user) => {
        await user.click(screen.getByRole('radio', { name: '1/2' }))
      },
      expectedValue: 12.7,
    },
  ]

  return (name) => cases.find((entry) => entry.name === name)
}

const CONTROL_NAMES = [
  'TextField',
  'TextArea',
  'Select',
  'Checkbox',
  'Switch',
  'RadioGroup',
  'DateField',
  'NumericStepper',
  'FractionInput',
  'MeasurementField',
] as const

function describedByOf(element: HTMLElement): readonly string[] {
  return (element.getAttribute('aria-describedby') ?? '').split(' ').filter((id) => id !== '')
}

/** The text a screen reader would read as the field's description, in the order it reads it. */
function describedText(element: HTMLElement): string {
  return describedByOf(element)
    .map((id) => document.getElementById(id)?.textContent ?? '')
    .join(' ')
}

describe.each(CONTROL_NAMES)('the FieldProps contract — %s', (controlName) => {
  const onValueChange = vi.fn()
  const control = makeCase(onValueChange)(controlName)
  if (control === undefined) {
    throw new Error(`No contract case for ${controlName}`)
  }

  it('renders the visible label and puts it in the accessible name', () => {
    renderWithProviders(control.render({}))

    expect(screen.getByText(LABEL, { exact: false })).toBeInTheDocument()
    control.assertAccessibleName()
  })

  it('has no placeholder anywhere — a placeholder is never a label', () => {
    const { container } = renderWithProviders(control.render({}))

    expect(container.querySelectorAll('[placeholder]')).toHaveLength(0)
  })

  it('honours an explicit id, which is what the error summary links to', () => {
    renderWithProviders(control.render({ id: 'waist-control' }))

    expect(control.host()).toHaveAttribute('id', 'waist-control')
  })

  it('wires the description into aria-describedby', () => {
    renderWithProviders(control.render({ description: 'Round the natural waist.' }))

    const host = (control.describedByHost ?? control.host)()
    expect(describedText(host)).toContain('Round the natural waist.')
  })

  it('marks an error invalid, in words, and reads it before the description', () => {
    renderWithProviders(
      control.render({
        description: 'Round the natural waist.',
        error: 'Waist must be between 45.0 cm and 150.0 cm.',
      }),
    )

    expect(control.host()).toHaveAttribute('aria-invalid', 'true')

    const host = (control.describedByHost ?? control.host)()
    const spoken = describedText(host)
    expect(spoken).toContain('Waist must be between 45.0 cm and 150.0 cm.')
    expect(spoken).toContain('Error:')
    expect(spoken.indexOf('Waist must be')).toBeLessThan(spoken.indexOf('Round the natural waist.'))
  })

  it('reads a warning without making the field invalid', () => {
    renderWithProviders(control.render({ warning: 'Outside the usual range.' }))

    expect(control.host()).not.toHaveAttribute('aria-invalid')
    const host = (control.describedByHost ?? control.host)()
    expect(describedText(host)).toContain('Outside the usual range.')
  })

  it('announces required through aria-required and shows the word', () => {
    const { container } = renderWithProviders(control.render({ required: true }))

    expect(control.host()).toHaveAttribute('aria-required', 'true')
    expect(container).toHaveTextContent('Required')
    // The marker is visible and aria-hidden, so the accessible name is exactly what it was.
    control.assertAccessibleName()
  })

  it('appends external describedByIds after its own', () => {
    renderWithProviders(
      <>
        <p id="step-note">Both sides of the tape must be level.</p>
        {control.render({ description: 'Round the natural waist.', describedByIds: ['step-note'] })}
      </>,
    )

    const host = (control.describedByHost ?? control.host)()
    expect(describedByOf(host)).toContain('step-note')
  })

  it('disables the control when asked', () => {
    renderWithProviders(control.render({ disabled: true }))

    expect(control.host()).toBeDisabled()
  })

  it('marks itself read-only rather than pretending to be editable', () => {
    renderWithProviders(control.render({ readOnly: true }))

    const host = control.host()
    if (control.nativeReadOnly) {
      expect(host).toHaveAttribute('readonly')
    } else {
      expect(host).toHaveAttribute('aria-readonly', 'true')
    }
  })

  it('reports a change through onValueChange', async () => {
    const user = userEvent.setup()
    renderWithProviders(control.render({}))
    onValueChange.mockClear()

    await control.interact(user)

    expect(onValueChange).toHaveBeenCalledWith(control.expectedValue)
  })

  it('passes axe with a description, and again with an error', async () => {
    const clean = renderWithProviders(control.render({ description: 'Round the natural waist.' }))
    await expectNoAccessibilityViolations(clean.container)
    clean.unmount()

    const invalid = renderWithProviders(
      control.render({ error: 'Waist must be between 45.0 cm and 150.0 cm.', required: true }),
    )
    await expectNoAccessibilityViolations(invalid.container)
  })

  it('renders in the pseudo-locale, which is how 40% text growth is proved', () => {
    const { container } = renderWithProviders(control.render({ required: true }), {
      locale: PSEUDO_LOCALE,
    })

    // The label is the caller's own string and stays as it is; every catalogue string around it
    // grows and gains accents, so anything still plain English here is hard-coded in a component.
    expect(container).toHaveTextContent(LABEL)
    expect(container.textContent).not.toContain('Required')
  })
})

describe('the unit adornment', () => {
  const WITH_UNIT = { symbol: 'cm', label: 'centimetres' } as const

  it.each(['TextField', 'TextArea', 'Select', 'NumericStepper'] as const)(
    'shows the symbol and speaks the word — %s',
    (controlName) => {
      const control = makeCase(vi.fn())(controlName)
      if (control === undefined || !control.acceptsUnit) {
        throw new Error(`${controlName} should accept a unit`)
      }
      renderWithProviders(control.render({ unit: WITH_UNIT }))

      expect(screen.getByText('cm')).toBeInTheDocument()
      const host = (control.describedByHost ?? control.host)()
      expect(describedText(host)).toContain('centimetres')
    },
  )

  it('reads the unit before the description, because it changes what the number means', () => {
    const control = makeCase(vi.fn())('TextField')
    if (control === undefined) {
      throw new Error('missing case')
    }
    renderWithProviders(control.render({ unit: WITH_UNIT, description: 'Round the waist.' }))

    const spoken = describedText(control.host())
    expect(spoken.indexOf('centimetres')).toBeLessThan(spoken.indexOf('Round the waist.'))
  })
})
