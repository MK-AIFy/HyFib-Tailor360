import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { useCurrentUser } from '../../auth/useSession'
import { readCurrentCatalog } from '../../catalog/catalogApi'
import { Icon } from '../../components/primitives/Icon'
import type { IconName } from '../../components/primitives/icons'
import type { OrderableCatalog } from '../../catalog/types'
import './workspace.css'

interface Workflow {
  readonly path: string
  readonly title: string
  readonly description: string
  readonly icon: IconName
  readonly stage: string
  readonly connected: boolean
}

const workflows: readonly Workflow[] = [
  {
    path: '/customers',
    title: 'Customers',
    description: 'Find, register and review customer records.',
    icon: 'users',
    stage: 'Intake',
    connected: true,
  },
  {
    path: '/measurements',
    title: 'Measurements',
    description: 'Prepare approved templates and capture accurate fittings.',
    icon: 'ruler',
    stage: 'Intake',
    connected: false,
  },
  {
    path: '/orders',
    title: 'Orders',
    description: 'Build and confirm a garment order from a published catalogue.',
    icon: 'clipboard',
    stage: 'Fulfilment',
    connected: false,
  },
  {
    path: '/production',
    title: 'Production',
    description: 'Move garments through the tailoring workflow and quality checks.',
    icon: 'scissors',
    stage: 'Fulfilment',
    connected: false,
  },
  {
    path: '/inventory',
    title: 'Inventory',
    description: 'Trace materials, reservations and stock movements.',
    icon: 'package',
    stage: 'Fulfilment',
    connected: false,
  },
  {
    path: '/billing',
    title: 'Billing',
    description: 'Create GST bills, record payments and reconcile balances.',
    icon: 'receipt',
    stage: 'Finance',
    connected: false,
  },
  {
    path: '/delivery',
    title: 'Delivery',
    description: 'Verify payment and hand over finished garments.',
    icon: 'truck',
    stage: 'Finance',
    connected: false,
  },
  {
    path: '/reports',
    title: 'Reporting',
    description: 'Review operational and financial results.',
    icon: 'bar-chart',
    stage: 'Finance',
    connected: false,
  },
]

const secondary: Record<string, { title: string; description: string; icon: IconName }> = {
  scan: {
    title: 'Scan',
    description: 'Barcode custody starts when garments enter the order workflow.',
    icon: 'scan',
  },
  workboard: {
    title: 'Workboard',
    description: 'The live production queue will draw from confirmed garment jobs.',
    icon: 'layout',
  },
  settings: {
    title: 'Settings',
    description: 'Manage account security, display preferences and shop administration.',
    icon: 'settings',
  },
}

function WorkflowCard({ workflow }: { readonly workflow: Workflow }) {
  return (
    <Link className="workspace-feature" to={workflow.path}>
      <span className="workspace-feature__icon">
        <Icon name={workflow.icon} />
      </span>
      <span className="workspace-feature__body">
        <strong>{workflow.title}</strong>
        <span>{workflow.description}</span>
      </span>
      <span
        className={`workspace-feature__state${workflow.connected ? ' workspace-feature__state--connected' : ''}`}
      >
        {workflow.connected ? 'Connected' : 'In development'}
      </span>
      <span className="workspace-feature__arrow" aria-hidden="true">
        ↗
      </span>
    </Link>
  )
}

export function WorkspaceHomeRoute() {
  const user = useCurrentUser()
  const [catalog, setCatalog] = useState<OrderableCatalog | null>(null)
  const [catalogState, setCatalogState] = useState<'loading' | 'ready' | 'unavailable'>('loading')

  useEffect(() => {
    if (!user.permissions.includes('catalog.read') || user.branchId === null) {
      setCatalogState('unavailable')
      return
    }
    const controller = new AbortController()
    void readCurrentCatalog(controller.signal)
      .then((next) => {
        setCatalog(next)
        setCatalogState('ready')
      })
      .catch(() => {
        if (!controller.signal.aborted) setCatalogState('unavailable')
      })
    return () => controller.abort()
  }, [user.branchId, user.permissions])

  return (
    <div className="workspace-page">
      <section className="workspace-hero">
        <div>
          <span className="workspace-eyebrow">Your workspace</span>
          <h1>Good to see you, {user.displayName}.</h1>
          <p>
            Everything your shop does, in one clear journey. Start with a customer, check the
            catalogue, then follow each garment through to delivery.
          </p>
          <div className="workspace-hero__actions">
            {user.permissions.includes('customers.read') ? (
              <Link className="workspace-button workspace-button--light" to="/customers">
                Find a customer <span aria-hidden="true">↗</span>
              </Link>
            ) : null}
            {user.permissions.includes('catalog.edit') ? (
              <Link className="workspace-button workspace-button--outline" to="/admin/catalog">
                Manage catalogue
              </Link>
            ) : null}
          </div>
        </div>
        <div className="workspace-hero__mark" aria-hidden="true">
          <span>H</span>
          <span>360</span>
        </div>
      </section>

      <section className="workspace-status" aria-label="Live workspace status">
        <div className="workspace-status__item">
          <span>Signed in as</span>
          <strong>{user.displayName}</strong>
          <small>Verified session</small>
        </div>
        <div className="workspace-status__item">
          <span>Current branch</span>
          <strong>{user.branchId === null ? 'Choose a branch' : 'Branch selected'}</strong>
          <small>
            {user.branchId === null
              ? 'Branch-specific work is unavailable'
              : 'Scoped to your account'}
          </small>
        </div>
        <div className="workspace-status__item">
          <span>Orderable services</span>
          <strong>{catalogState === 'ready' ? String(catalog?.services.length ?? 0) : '—'}</strong>
          <small>
            {catalogState === 'ready'
              ? catalog?.catalogVersionId
                ? 'From the published catalogue'
                : 'No catalogue published'
              : catalogState === 'loading'
                ? 'Checking catalogue…'
                : 'Unavailable for this account'}
          </small>
        </div>
      </section>

      <section className="workspace-flow" aria-labelledby="workflow-title">
        <div className="workspace-section-heading">
          <div>
            <span className="workspace-eyebrow">Shop operations</span>
            <h2 id="workflow-title">Move work forward</h2>
          </div>
          <p>Each step has a clear place and status.</p>
        </div>
        {['Intake', 'Fulfilment', 'Finance'].map((stage, index) => (
          <div className="workspace-flow__stage" key={stage}>
            <div className="workspace-flow__label">
              <span>0{index + 1}</span>
              <h3>{stage}</h3>
            </div>
            <div className="workspace-flow__cards">
              {workflows
                .filter((workflow) => workflow.stage === stage)
                .map((workflow) => (
                  <WorkflowCard key={workflow.path} workflow={workflow} />
                ))}
            </div>
          </div>
        ))}
      </section>

      <section className="workspace-callout">
        <div>
          <span className="workspace-eyebrow">Manage the foundation</span>
          <h2>Keep reference data ready</h2>
          <p>
            Published catalogue and measurement templates make intake possible. Your administration
            tools are connected to the same backend as the shop workspace.
          </p>
        </div>
        {user.permissions.includes('catalog.edit') ? (
          <Link className="workspace-button" to="/admin/catalog">
            Open administration
          </Link>
        ) : null}
      </section>
    </div>
  )
}

export function WorkflowRoute({ name }: { readonly name: string }) {
  const user = useCurrentUser()
  const workflow = workflows.find((item) => item.path === `/${name}`)
  const fallback = secondary[name]
  const title = workflow?.title ?? fallback?.title ?? 'Workspace'
  const icon = workflow?.icon ?? fallback?.icon ?? 'layout'
  const description = workflow?.description ?? fallback?.description ?? ''
  const isMeasurements = name === 'measurements'
  const isSettings = name === 'settings'

  return (
    <section className="workspace-page">
      <Link className="workspace-back" to="/">
        ← Workspace
      </Link>
      <div className="workspace-heading">
        <div>
          <span className="workspace-eyebrow">Shop workflow</span>
          <h1>{title}</h1>
          <p>{description}</p>
        </div>
        <span className="workspace-heading__icon">
          <Icon name={icon} />
        </span>
      </div>
      <div className="workspace-detail-grid">
        <div className="workspace-card workspace-card--accent">
          <span className="workspace-eyebrow">
            {isSettings ? 'Connected tools' : isMeasurements ? 'Preparation' : 'In development'}
          </span>
          <h2>
            {isSettings
              ? 'Manage your workspace'
              : isMeasurements
                ? 'Prepare measurement capture'
                : `${title} workflow is being built`}
          </h2>
          <p>
            {isSettings
              ? 'Account, display and administration screens use the active server session.'
              : isMeasurements
                ? 'Templates need an approved published version before measurements can be captured. Administrators can review them now.'
                : 'The application has a foundation for this module, but no server operations for this journey yet. Actions will appear when the corresponding API and business rules are ready.'}
          </p>
        </div>
        <div className="workspace-card">
          <h2>Available now</h2>
          <div className="workspace-actions">
            {isSettings ? (
              <>
                <Link to="/settings/display">
                  Display preferences <span aria-hidden="true">↗</span>
                </Link>
                <Link to="/account/security">
                  Account security <span aria-hidden="true">↗</span>
                </Link>
                {user.permissions.includes('admin.users') ? (
                  <Link to="/admin/users">
                    Staff administration <span aria-hidden="true">↗</span>
                  </Link>
                ) : null}
              </>
            ) : null}
            {isMeasurements && user.permissions.includes('catalog.templates.edit') ? (
              <Link to="/admin/templates">
                Measurement templates <span aria-hidden="true">↗</span>
              </Link>
            ) : null}
            {user.permissions.includes('customers.read') ? (
              <Link to="/customers">
                Find a customer <span aria-hidden="true">↗</span>
              </Link>
            ) : null}
            {user.permissions.includes('catalog.edit') ? (
              <Link to="/admin/catalog">
                Review catalogue <span aria-hidden="true">↗</span>
              </Link>
            ) : null}
            <Link to="/">
              Return to workspace <span aria-hidden="true">↗</span>
            </Link>
          </div>
        </div>
      </div>
    </section>
  )
}
