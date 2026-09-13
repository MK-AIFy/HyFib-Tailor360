import { FormattedMessage } from 'react-intl'
import { Link, createBrowserRouter, isRouteErrorResponse, useRouteError } from 'react-router'
import { App } from '../App'
import { RequireSession } from '../auth/RequireSession'
import { DisplayPreferencesPanel } from '../components/layout/DisplayPreferencesPanel'
import { ADMIN_PERMISSIONS } from '../admin/adminPermissions'
import { RequirePermission } from '../admin/RequirePermission'
import { BILLING_PERMISSIONS } from '../billing/billingPermissions'
import { AboutRoute } from '../routes/AboutRoute'
import { AllocateAdvanceRoute } from '../routes/billing/AllocateAdvanceRoute'
import { CashierSessionRoute } from '../routes/billing/CashierSessionRoute'
import { DispatchExceptionApprovalRoute } from '../routes/billing/DispatchExceptionApprovalRoute'
import { OutstandingBalancesRoute } from '../routes/billing/OutstandingBalancesRoute'
import { PaymentDetailRoute } from '../routes/billing/PaymentDetailRoute'
import { ReconciliationApprovalRoute } from '../routes/billing/ReconciliationApprovalRoute'
import { TakePaymentRoute } from '../routes/billing/TakePaymentRoute'
import { AdminShell } from '../routes/admin/AdminShell'
import { AuditTrailRoute } from '../routes/admin/AuditTrailRoute'
import { BranchListRoute } from '../routes/admin/BranchListRoute'
import { FeatureFlagRoute } from '../routes/admin/FeatureFlagRoute'
import { OutboxRoute } from '../routes/admin/OutboxRoute'
import { RoleDetailRoute } from '../routes/admin/RoleDetailRoute'
import { RoleListRoute } from '../routes/admin/RoleListRoute'
import { StaffDetailRoute } from '../routes/admin/StaffDetailRoute'
import { StaffListRoute } from '../routes/admin/StaffListRoute'
import { CatalogDesignRoute } from '../routes/catalog/CatalogDesignRoute'
import { CatalogVersionEditorRoute } from '../routes/catalog/CatalogVersionEditorRoute'
import { CatalogVersionListRoute } from '../routes/catalog/CatalogVersionListRoute'
import { TemplateVersionEditorRoute } from '../routes/admin/TemplateVersionEditorRoute'
import { TemplateDetailRoute } from '../routes/admin/TemplateDetailRoute'
import { TemplateListRoute } from '../routes/admin/TemplateListRoute'
import { InstallRoute } from '../routes/InstallRoute'
import { MEASUREMENT_PERMISSIONS } from '../measurements/measurementsPermissions'
import { MeasurementCompareRoute } from '../routes/measurements/MeasurementCompareRoute'
import { MeasurementDraftRoute } from '../routes/measurements/MeasurementDraftRoute'
import { MeasurementSheetRoute } from '../routes/measurements/MeasurementSheetRoute'
import { MeasurementStartRoute } from '../routes/measurements/MeasurementStartRoute'
import { MeasurementsHomeRoute } from '../routes/measurements/MeasurementsHomeRoute'
import { AuthShell } from '../routes/auth/AuthShell'
import { AuthenticatorEnrolmentRoute } from '../routes/auth/AuthenticatorEnrolmentRoute'
import { LoginRoute } from '../routes/auth/LoginRoute'
import { MfaChallengeRoute } from '../routes/auth/MfaChallengeRoute'
import { RecoveryConfirmRoute } from '../routes/auth/RecoveryConfirmRoute'
import { RecoveryRequestRoute } from '../routes/auth/RecoveryRequestRoute'
import { SecurityRoute } from '../routes/auth/SecurityRoute'
import { SessionsRoute } from '../routes/auth/SessionsRoute'

/** Placeholder home screen. The role dashboards arrive with #50 and the feature milestones. */
export function HomeRoute() {
  return (
    <section className="page">
      <h1>
        <FormattedMessage id="home.title" />
      </h1>
      <p>
        <FormattedMessage id="home.body" />
      </p>
    </section>
  )
}

/**
 * The display-preferences screen: theme, text size and row density.
 *
 * It lives in the shell rather than behind a role's Settings destination, and the shell's utilities
 * slot links to it from every layout. Only three of the eight journey roles have a Settings
 * destination at all, and the person who needs 150% text or the high-contrast sunlight theme is at
 * least as likely to be a Tailor in a workshop as an Owner at a desk.
 */
export function DisplaySettingsRoute() {
  return (
    <section className="page">
      <h1>
        <FormattedMessage id="layout.settings.display" />
      </h1>
      <p>
        <FormattedMessage id="layout.settings.displayBody" />
      </p>
      <DisplayPreferencesPanel />
    </section>
  )
}

export function NotFoundRoute() {
  return (
    <section className="page">
      <h1>
        <FormattedMessage id="notFound.title" />
      </h1>
      <p>
        <FormattedMessage id="notFound.body" />
      </p>
      <p>
        <Link to="/">
          <FormattedMessage id="notFound.back" />
        </Link>
      </p>
    </section>
  )
}

/**
 * The last line of defence for a render or loader failure. It shows plain language and never the
 * exception text, which can carry identifiers or personal data.
 */
export function RouteErrorBoundary() {
  const error = useRouteError()
  const notFound = isRouteErrorResponse(error) && error.status === 404

  return (
    <section className="page">
      <h1>
        <FormattedMessage id={notFound ? 'notFound.title' : 'error.title'} />
      </h1>
      <p>
        <FormattedMessage id={notFound ? 'notFound.body' : 'error.body'} />
      </p>
      <p>
        <Link to="/">
          <FormattedMessage id="notFound.back" />
        </Link>
      </p>
    </section>
  )
}

/**
 * The data router. Data routers are used from the start (rather than plain <Routes>) because loaders,
 * actions and the pending/error states of later screens depend on them.
 *
 * ## Two frames, and the line between them
 *
 * The signing-in screens are their own branch, outside the application shell. A person who cannot
 * sign in must not be shown a navigation bar of destinations that will refuse them — every link is a
 * dead end, and a screen full of dead ends reads as a broken application. `AuthShell` gives them a
 * main landmark, the product name and the training banner, which is the one piece of chrome that
 * genuinely matters before somebody types a password.
 *
 * Everything inside the shell that is about the shop is behind `RequireSession`. `install` and
 * `about` deliberately are not: the person who most needs the install instructions is the one who
 * cannot yet sign in on a device they have not installed the application on.
 */
export const router = createBrowserRouter([
  {
    element: <AuthShell />,
    errorElement: <RouteErrorBoundary />,
    children: [
      { path: 'sign-in', element: <LoginRoute /> },
      { path: 'sign-in/verify', element: <MfaChallengeRoute /> },
      { path: 'recovery', element: <RecoveryRequestRoute /> },
      { path: 'recovery/confirm', element: <RecoveryConfirmRoute /> },
    ],
  },
  {
    path: '/',
    element: <App />,
    errorElement: <RouteErrorBoundary />,
    children: [
      // Reachable without a session, because the person who needs them most is the one who cannot
      // sign in on a device they have not installed yet.
      { path: 'install', element: <InstallRoute /> },
      { path: 'about', element: <AboutRoute /> },
      // Display settings are deliberately outside the guard as well. Somebody who needs 150% text or
      // the high-contrast sunlight theme needs it *to read the sign-in screen*, and a preference
      // that can only be reached after signing in is a preference they cannot reach at all. Nothing
      // on that screen is personal data: it is a device-local stub until #25 gives it a home on the
      // user record.
      { path: 'settings/display', element: <DisplaySettingsRoute /> },
      {
        element: <RequireSession />,
        children: [
          { index: true, element: <HomeRoute /> },
          { path: 'account/security', element: <SecurityRoute /> },
          { path: 'account/security/authenticator', element: <AuthenticatorEnrolmentRoute /> },
          { path: 'account/sessions', element: <SessionsRoute /> },
          // Measuring a customer (#123). Three addresses: the destination the shells navigate to,
          // the start screen the phone shell's primary action opens, and the draft itself — which
          // has an address of its own because a draft is shared within the branch and survives an
          // interruption, so a colleague can pick it up on their own device.
          {
            path: 'measurements',
            element: (
              <RequirePermission permission={MEASUREMENT_PERMISSIONS.capture}>
                <MeasurementsHomeRoute />
              </RequirePermission>
            ),
          },
          {
            path: 'measurements/new',
            element: (
              <RequirePermission permission={MEASUREMENT_PERMISSIONS.capture}>
                <MeasurementStartRoute />
              </RequirePermission>
            ),
          },
          {
            path: 'measurements/drafts/:draftId',
            element: (
              <RequirePermission permission={MEASUREMENT_PERMISSIONS.capture}>
                <MeasurementDraftRoute />
              </RequirePermission>
            ),
          },
          {
            path: 'measurements/compare/:beforeId/:afterId',
            element: (
              <RequirePermission permission={MEASUREMENT_PERMISSIONS.capture}>
                <MeasurementCompareRoute />
              </RequirePermission>
            ),
          },
          {
            // The sheet is gated on the narrower key: a sheet is the widest audience a measurement
            // gets, and the right to produce one is held by fewer people than the right to take one.
            path: 'measurements/:versionId/sheet',
            element: (
              <RequirePermission permission={MEASUREMENT_PERMISSIONS.readSheet}>
                <MeasurementSheetRoute />
              </RequirePermission>
            ),
          },
          // Billing (#161-#165): taking a payment, outstanding balances, the cashier session,
          // reconciliation approval and the dispatch exception the Owner approves. Every write here
          // is online-only (`OfflineBlockedAction`) — billing, payment and inventory reconciliation
          // are never queued (plan Section 4.6).
          {
            path: 'billing/outstanding',
            element: (
              <RequirePermission permission={BILLING_PERMISSIONS.createInvoice}>
                <OutstandingBalancesRoute />
              </RequirePermission>
            ),
          },
          {
            path: 'billing/payments/new',
            element: (
              <RequirePermission permission={BILLING_PERMISSIONS.recordPayment}>
                <TakePaymentRoute />
              </RequirePermission>
            ),
          },
          {
            path: 'billing/payments/:paymentId',
            element: (
              <RequirePermission permission={BILLING_PERMISSIONS.recordPayment}>
                <PaymentDetailRoute />
              </RequirePermission>
            ),
          },
          {
            // Moving a held advance to a posted invoice by hand, against the automatic rule: a
            // narrower key than reading the payment, and step-up on the server regardless.
            path: 'billing/payments/:paymentId/allocate',
            element: (
              <RequirePermission permission={BILLING_PERMISSIONS.allocateAdvanceManual}>
                <AllocateAdvanceRoute />
              </RequirePermission>
            ),
          },
          {
            path: 'billing/cashier',
            element: (
              <RequirePermission permission={BILLING_PERMISSIONS.cashierSession}>
                <CashierSessionRoute />
              </RequirePermission>
            ),
          },
          {
            path: 'billing/cashier-sessions/:sessionId/reconciliation',
            element: (
              <RequirePermission permission={BILLING_PERMISSIONS.approveReconciliation}>
                <ReconciliationApprovalRoute />
              </RequirePermission>
            ),
          },
          {
            // The Owner only, per the plan's interim position (raci.md footnote (15), XQ-02).
            path: 'billing/dispatch-exceptions/new',
            element: (
              <RequirePermission permission={BILLING_PERMISSIONS.approveDispatchException}>
                <DispatchExceptionApprovalRoute />
              </RequirePermission>
            ),
          },
          // The administration section. Each screen guards itself as well as being filtered out of
          // the sub-navigation, and the server guards itself again — three layers, of which only the
          // innermost is the authorisation.
          {
            path: 'admin',
            element: <AdminShell />,
            children: [
              {
                path: 'users',
                element: (
                  <RequirePermission permission={ADMIN_PERMISSIONS.users}>
                    <StaffListRoute />
                  </RequirePermission>
                ),
              },
              {
                path: 'users/:userId',
                element: (
                  <RequirePermission permission={ADMIN_PERMISSIONS.users}>
                    <StaffDetailRoute />
                  </RequirePermission>
                ),
              },
              {
                path: 'branches',
                element: (
                  <RequirePermission permission={ADMIN_PERMISSIONS.branches}>
                    <BranchListRoute />
                  </RequirePermission>
                ),
              },
              {
                path: 'roles',
                element: (
                  <RequirePermission permission={ADMIN_PERMISSIONS.roles}>
                    <RoleListRoute />
                  </RequirePermission>
                ),
              },
              {
                path: 'roles/:roleId',
                element: (
                  <RequirePermission permission={ADMIN_PERMISSIONS.roles}>
                    <RoleDetailRoute />
                  </RequirePermission>
                ),
              },
              {
                path: 'templates',
                element: (
                  <RequirePermission permission={ADMIN_PERMISSIONS.templatesEdit}>
                    <TemplateListRoute />
                  </RequirePermission>
                ),
              },
              {
                // Guarded on the drafting permission, not the publishing one. Both are granted to
                // the same two roles, and a reviewer who could not read the version they are being
                // asked to approve would be a shape this product does not have.
                path: 'templates/:templateId',
                element: (
                  <RequirePermission permission={ADMIN_PERMISSIONS.templatesEdit}>
                    <TemplateDetailRoute />
                  </RequirePermission>
                ),
              },
              {
                // The draft editor, on an address of its own: a version is a thing a person works
                // *in* here rather than looks at, over several minutes and across an interruption,
                // so it survives a reload and can be shared with a colleague (#102).
                path: 'templates/:templateId/versions/:versionId',
                element: (
                  <RequirePermission permission={ADMIN_PERMISSIONS.templatesEdit}>
                    <TemplateVersionEditorRoute />
                  </RequirePermission>
                ),
              },
              {
                path: 'catalog',
                element: (
                  <RequirePermission permission={ADMIN_PERMISSIONS.catalogEdit}>
                    <CatalogVersionListRoute />
                  </RequirePermission>
                ),
              },
              {
                // Guarded on the drafting permission, not the publishing one — the same shape the
                // measurement templates use, and for the same reason: a reviewer who could not read
                // the version they are being asked to approve would be a product this shop does not
                // have. The publishing acts themselves are hidden without the second key.
                path: 'catalog/:versionId',
                element: (
                  <RequirePermission permission={ADMIN_PERMISSIONS.catalogEdit}>
                    <CatalogVersionEditorRoute />
                  </RequirePermission>
                ),
              },
              {
                // Groups, options and rules for one category, on an address of its own (#141) — the
                // same reasoning the version editor itself gets one: a working set of rules survives
                // a reload. Guarded the same way, on the drafting key rather than the publishing one.
                path: 'catalog/:versionId/categories/:categoryId/design',
                element: (
                  <RequirePermission permission={ADMIN_PERMISSIONS.catalogEdit}>
                    <CatalogDesignRoute />
                  </RequirePermission>
                ),
              },
              {
                path: 'features',
                element: (
                  <RequirePermission permission={ADMIN_PERMISSIONS.featureFlags}>
                    <FeatureFlagRoute />
                  </RequirePermission>
                ),
              },
              {
                path: 'audit',
                element: (
                  <RequirePermission permission={ADMIN_PERMISSIONS.auditRead}>
                    <AuditTrailRoute />
                  </RequirePermission>
                ),
              },
              {
                path: 'outbox',
                element: (
                  <RequirePermission permission={ADMIN_PERMISSIONS.outboxReplay}>
                    <OutboxRoute />
                  </RequirePermission>
                ),
              },
            ],
          },
        ],
      },
      // A client-side 404: the server serves the shell for any unknown path. It stays last.
      { path: '*', element: <NotFoundRoute /> },
    ],
  },
])
