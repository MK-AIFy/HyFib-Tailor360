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
import { Switch } from '../../design-system/components/forms/Switch'
import type { ChoiceOption } from '../../design-system/components/forms/controlTypes'
import { useDisplayPreferences } from '../../app/useDisplayPreferences'
import { OfflineBlockedAction } from '../states/OfflineBlockedAction'
import { RetryableError } from '../states/RetryableError'
import './DisplayPreferencesPanel.css'

export interface DisplayPreferencesPanelProps {
  readonly className?: string
}

/**
 * The screen where a person sets the theme, the text size, the row density and reduced motion.
 *
 * ## Why these four and not a slider
 *
 * They are what the product owes. docs/nfr/accessibility-localisation.md section 4.1 requires a
 * high-contrast theme for reading a screen in afternoon sunlight at the counter, and checklist item
 * A11Y-54 is a person judging that outdoors at arm's length. Checklist item A11Y-72 tests the
 * product's own 100 / 125 / 150% text size — separate from browser zoom, because that is the one a
 * person with presbyopia actually finds and switches on. Density is the desktop affordance that
 * makes a dense table dense, and its compact setting collapses back to the 44 px standard target on
 * a coarse pointer, so it cannot reach a phone however it is set. Reduced motion adds a way to ask
 * for less animation than the device already gives; it never takes the device's own answer away.
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
 *
 * ## Two homes for the same four settings
 *
 * `useDisplayPreferences().accountBacked` says which one applies. On this device only, the note below
 * the controls says so and nothing else changes — this is the whole of what an anonymous person, or
 * an isolated test or story with its own store, ever sees. Saved to the signed-in account, the panel
 * also owns the outcome of that save: a quiet `role="status"` line for the ordinary case, and
 * `RetryableError` or `OfflineBlockedAction` for the two ways a save can fail to land. Neither of
 * those is reached through the shell's own status channel (`useShellStatus` throws outside
 * `AppShell`, and this screen is reachable anonymously and rendered standalone in its own tests and
 * stories), so the panel raises them itself.
 */
export function DisplayPreferencesPanel({ className }: DisplayPreferencesPanelProps) {
  const intl = useIntl()
  const {
    preferences,
    loaded,
    accountBacked,
    saveStatus,
    saveError,
    retrySave,
    setTheme,
    setTextSize,
    setDensity,
    setReducedMotion,
  } = useDisplayPreferences()

  const controlsDisabled = !loaded

  const themeOptions: ChoiceOption[] = THEME_PREFERENCES.map((theme) => ({
    value: theme,
    label: intl.formatMessage({ id: `layout.display.theme.${theme}` }),
    disabled: controlsDisabled,
  }))

  const textSizeOptions: ChoiceOption[] = TEXT_SIZE_PREFERENCES.map((size) => ({
    value: size,
    label: intl.formatMessage({ id: `layout.display.textSize.${size}` }),
    disabled: controlsDisabled,
  }))

  const densityOptions: ChoiceOption[] = DENSITIES.map((density) => ({
    value: density,
    label: intl.formatMessage({ id: `layout.display.density.${density}` }),
    disabled: controlsDisabled,
  }))

  const saveAction = intl.formatMessage({ id: 'layout.display.saveAction' })

  return (
    <div className={cx('display-preferences', className)}>
      <RadioGroup
        disabled={controlsDisabled}
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
        disabled={controlsDisabled}
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
        disabled={controlsDisabled}
        name="displayDensity"
        label={intl.formatMessage({ id: 'layout.display.density' })}
        description={intl.formatMessage({ id: 'layout.display.densityDescription' })}
        options={densityOptions}
        value={preferences.density}
        onValueChange={(value) => {
          setDensity(value as Density)
        }}
      />

      <Switch
        description={intl.formatMessage({ id: 'layout.display.reducedMotionDescription' })}
        disabled={controlsDisabled}
        label={intl.formatMessage({ id: 'layout.display.reducedMotion' })}
        name="displayReducedMotion"
        value={preferences.reducedMotion}
        onValueChange={setReducedMotion}
      />

      {/* The change is visible here without leaving the screen — a job number, a date and an amount,
          which is exactly the shop-floor text A11Y-54 asks a person to read in sunlight. */}
      <p className="display-preferences__sample">
        {intl.formatMessage({ id: 'layout.display.sample' })}
      </p>

      <p className="display-preferences__note">
        {intl.formatMessage({
          id: accountBacked ? 'layout.display.account' : 'layout.display.storage',
        })}
      </p>

      {accountBacked ? (
        <>
          {/* A quiet confirmation for the ordinary case. Omitted for `failed` and `offline`, whose
              own alert already carries a role of its own — a second status region announcing the same
              event would be two live regions disagreeing about how urgent it is. */}
          {saveStatus === 'saved' ? (
            <p aria-live="polite" className="display-preferences__save-status" role="status">
              {intl.formatMessage({ id: 'layout.display.saved' })}
            </p>
          ) : null}

          {saveStatus === 'failed' ? (
            <RetryableError
              action={saveAction}
              onRetry={retrySave}
              {...(saveError?.status === undefined ? { cause: 'network' as const } : {})}
              {...(saveError?.problem === undefined ? {} : { problem: saveError.problem })}
            >
              <p className="state-line">{intl.formatMessage({ id: 'layout.display.notSaved' })}</p>
            </RetryableError>
          ) : null}

          {saveStatus === 'offline' ? (
            <OfflineBlockedAction action={saveAction} onRetry={retrySave} />
          ) : null}
        </>
      ) : null}
    </div>
  )
}
