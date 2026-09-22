import { useIntl } from 'react-intl'
import { Outlet, useMatch, useNavigate } from 'react-router'
import { MasterDetail } from '../../components/layout/MasterDetail'
import type { MasterDetailArrangement } from '../../components/layout/MasterDetail'
import { CustomerSearchRoute } from './CustomerSearchRoute'
import './customers.css'

/**
 * The customer list beside the record it opens (#26, #182, #616).
 *
 * ## Why this exists rather than two addresses that replace one another
 *
 * A receptionist at a counter is usually deciding *which* of two people is standing in front of
 * them, and the whole of that decision is comparing a record against the rest of the results. A
 * layout where opening a record throws the results away makes them search again to look at the
 * second one, with a customer waiting — which is the case the tablet master-detail layout in the #50
 * blueprint and `docs/nfr/support-matrix.md` section 5 exists for.
 *
 * `MasterDetail` was built for it and had no consumers until now. It measures **its own container**
 * — not the window, not the device — and splits at 768 CSS px, which is why a counter tablet in
 * portrait stacks (the shell's navigation leaves about 540 px) and the same tablet in landscape
 * splits. Nothing here reads an orientation, so 1.3.4 cannot be broken by accident.
 *
 * ## Why only the record is a pane
 *
 * `customers/new`, `:customerId/edit`, `:customerId/duplicates` and `:customerId/consent` stay
 * full-page addresses and are deliberately not panes. Each is a separate decision carrying a reason,
 * a confirmation or a step-up, and none of them is a thing to do while half-reading a list — a merge
 * offered in a side pane beside the list of candidates is precisely the accident that screen is
 * written to prevent.
 *
 * So the nesting is one level and one child: the record. Everything else navigates away, and comes
 * back to the list when it is done.
 */
export function CustomersLayoutRoute({
  arrangement,
}: {
  /** Forces an arrangement. For stories and tests only; the real decision is the measured width. */
  readonly arrangement?: MasterDetailArrangement
}) {
  const intl = useIntl()
  const navigate = useNavigate()

  // The record's own address, and only it. The full-page addresses above are not children of this
  // route, so they do not match here and cannot open as a pane.
  const selected = useMatch('/customers/:customerId')

  return (
    <MasterDetail
      className="customers__masterDetail"
      {...(arrangement === undefined ? {} : { arrangement })}
      detail={selected === null ? undefined : <Outlet />}
      detailLabel={intl.formatMessage({ id: 'customers.layout.detail' })}
      detailOpen={selected !== null}
      list={<CustomerSearchRoute />}
      listLabel={intl.formatMessage({ id: 'customers.layout.list' })}
      onCloseDetail={() => {
        void navigate('/customers')
      }}
    />
  )
}
