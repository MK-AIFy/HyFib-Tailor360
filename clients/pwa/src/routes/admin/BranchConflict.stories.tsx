import type { Meta, StoryObj } from '@storybook/react-vite'
import { aBranch } from '../../admin/testing/fixtures'
import {
  STORY_USER,
  storyJson,
  storyProblem,
  withAdminApi,
} from '../../admin/testing/storyTransport'
import { BranchListRoute } from './BranchListRoute'
import './admin.css'

const meta = {
  title: 'Administration/Branch conflict',
  parameters: { layout: 'padded' },
} satisfies Meta

export default meta
type Story = StoryObj<typeof meta>

/** Close the branch to see the stale-read refusal; Reload never repeats the command. */
export const ChangedSinceRead: Story = {
  render: () =>
    withAdminApi(<BranchListRoute />, {
      'GET /api/v1/me': () =>
        storyJson({
          ...STORY_USER,
          session: {
            idleExpiresAt: new Date(Date.now() + 3_600_000).toISOString(),
            absoluteExpiresAt: new Date(Date.now() + 43_200_000).toISOString(),
            warningLeadSeconds: 120,
            mfaSatisfied: true,
          },
        }),
      'GET /api/v1/admin/branches/': () => storyJson([aBranch()]),
      [`POST /api/v1/admin/branches/${aBranch().branchId}/close`]: () =>
        storyProblem(409, 'identity.concurrent-change'),
    }),
}

export const TextGrowth: Story = {
  ...ChangedSinceRead,
  globals: { locale: 'en-XA' },
}
