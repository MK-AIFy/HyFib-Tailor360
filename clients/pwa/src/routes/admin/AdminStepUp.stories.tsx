import type { Meta, StoryObj } from '@storybook/react-vite'
import { aFeatureFlag } from '../../admin/testing/fixtures'
import {
  STORY_USER,
  storyJson,
  storyProblem,
  withAdminApi,
} from '../../admin/testing/storyTransport'
import { FeatureFlagRoute } from './FeatureFlagRoute'
import './admin.css'

const meta = {
  title: 'Administration/Identity recovery',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

/** Synthetic transport only: any entered password completes the preview's identity question. */
function preview(refuseAgain: boolean) {
  const flag = aFeatureFlag()
  let proved = false
  let enabled = false
  const session = {
    idleExpiresAt: new Date(Date.now() + 3_600_000).toISOString(),
    absoluteExpiresAt: new Date(Date.now() + 43_200_000).toISOString(),
    warningLeadSeconds: 120,
    mfaSatisfied: true,
  }
  return withAdminApi(<FeatureFlagRoute />, {
    'GET /api/v1/me': () => storyJson({ ...STORY_USER, session }),
    'GET /api/v1/admin/feature-flags/': () => storyJson([{ ...flag, enabled }]),
    [`PUT /api/v1/admin/feature-flags/${flag.key}`]: () => {
      if (!proved || refuseAgain) {
        return storyProblem(403, 'security.step-up-required')
      }
      enabled = true
      return storyJson({ ...flag, enabled }, 'W/"2"')
    },
    'POST /api/v1/auth/login': () => {
      proved = true
      return storyJson({
        step: 'complete',
        userId: STORY_USER.userId,
        displayName: STORY_USER.displayName,
        mustChangePassword: false,
        factors: STORY_USER.security.factors,
        session,
      })
    },
  })
}

export const FreshIdentityRequired: Story = { render: () => preview(false) }
export const RefusedAfterProof: Story = { render: () => preview(true) }
export const TextGrowth: Story = { ...FreshIdentityRequired, globals: { locale: 'en-XA' } }
