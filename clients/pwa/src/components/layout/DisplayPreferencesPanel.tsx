import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import {
  DENSITIES,
  TEXT_SIZE_PREFERENCES,
  THEME_PREFERENCES,
} from '../../design-system/foundations/types'
import type {
  Density,
  TextSizePreference,
  ThemePreference,
} from '../../design-system/foundations/types'
import { RadioGroup } from '../../design-system/components/forms/RadioGroup'
import type { ChoiceOption } from '../../design-system/components/forms/controlTypes'
import { useDisplayPreferences } from '../../app/useDisplayPreferences'
import './DisplayPreferencesPanel.css'

export interface DisplayPreferencesPanelProps {
  readonly className?: string
}

/**
 * The screen where a person sets the theme, the text size and the row density.
 *
 * ## Why these three and not a slider
 *
 * They are the three the product owes. docs/nfr/accessibility-localisation.md section 4.1 requires a
 * high-contrast theme for reading a screen in afternoon sunlight at the counter, and checklist item
 * A11Y-54 is a person judging that outdoors at arm's length. Checklist item A11Y-72 tests the
 * product's own 100 / 125 / 150% text size — separate from browser zoom, because that is the one a
 * person with presbyopia actually finds and switches on. Density is the desktop affordance that
 * makes a dense table dense, and its compact setting collapses back to the 44 px standard target on
 * a coarse pointer, so it cannot reach a phone however it is set.
 *
 * Radio groups rather than a select or a slider: every option is visible without opening anything,
 * each is a standard 44 px target, and the group name is announced before the first option
 * (checklist item A11Y-59). A slider would be a drag interaction, which 2.5.7 would then require an
 * alternative for.
 *
 * ## Applied immediately, and no Save button
 *
 * The change takes effect on the next frame. That is not a shortcut around 3.2.2 On Input — nothing
 * is submitted, no context changes, and no navigation happens; the person sees the setting they just
 * chose, which is the only way to judge whether it is the one they wanted. The sample line below the
 * controls is there so that the judgement can be made without leaving the screen.
 */
export function DisplayPreferencesPanel({ className }: DisplayPreferencesPanelProps) {
  const intl = useIntl()
  const { preferences, setTheme, setTextSize, setDensity } = useDisplayPreferences()

  const themeOptions: ChoiceOption[] = THEME_PREFERENCES.map((theme) => ({
    value: theme,
    label: intl.formatMessage({ id: `layout.display.theme.${theme}` }),
  }))

  const textSizeOptions: ChoiceOption[] = TEXT_SIZE_PREFERENCES.map((size) => ({
    value: size,
    label: intl.formatMessage({ id: `layout.display.textSize.${size}` }),
  }))

  const densityOptions: ChoiceOption[] = DENSITIES.map((density) => ({
    value: density,
    label: intl.formatMessage({ id: `layout.display.density.${density}` }),
  }))

  return (
    <div className={cx('display-preferences', className)}>
      <RadioGroup
        name="displayTheme"
        label={intl.formatMessage({ id: 'layout.display.theme' })}
        description={intl.formatMessage({ id: 'layout.display.themeDescription' })}
        options={themeOptions}
        value={preferences.theme}
        onValueChange={(value) => {
          setTheme(value as ThemePreference)
        }}
      />

      <RadioGroup
        name="displayTextSize"
        label={intl.formatMessage({ id: 'layout.display.textSize' })}
        description={intl.formatMessage({ id: 'layout.display.textSizeDescription' })}
        options={textSizeOptions}
        value={preferences.textSize}
        onValueChange={(value) => {
          setTextSize(value as TextSizePreference)
        }}
      />

      <RadioGroup
        name="displayDensity"
        label={intl.formatMessage({ id: 'layout.display.density' })}
        description={intl.formatMessage({ id: 'layout.display.densityDescription' })}
        options={densityOptions}
        value={preferences.density}
        onValueChange={(value) => {
          setDensity(value as Density)
        }}
      />

      {/* The change is visible here without leaving the screen — a job number, a date and an amount,
          which is exactly the shop-floor text A11Y-54 asks a person to read in sunlight. */}
      <p className="display-preferences__sample">
        {intl.formatMessage({ id: 'layout.display.sample' })}
      </p>

      <p className="display-preferences__note">
        {intl.formatMessage({ id: 'layout.display.storage' })}
      </p>
    </div>
  )
}
