import { FormattedMessage, useIntl } from 'react-intl'
import { Outlet, useLocation, useMatch, useNavigate } from 'react-router'
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
  // Kept across the close, for the reason the result links keep it: the committed search lives in
  // the address, so anything that navigates within this screen has to carry it or empty the list.
  const { search } = useLocation()

  // The record's own address, and only it. The full-page addresses above are not children of this
  // route, so they do not match here and cannot open as a pane.
  const selected = useMatch('/customers/:customerId')

  return (
    <>
      {/*
        The screen's own heading, and the reason it is here rather than in either pane.
        
        A heading that lives in a pane is only on the page when that pane is. When the panes cannot
        both fit and a record is open — a phone, a tablet in portrait, a shared link opened on
        either — `MasterDetail` takes the list *out of the DOM*, and with it went the only `h1`: the
        record's name was left as an `h2` under nothing. Owning it here makes the outline the same in
        both arrangements, which is the only version of it that is true.
        
        Visually hidden because the navigation already says where somebody is, and a second "Customers"
        above the panes would be chrome competing with the screen for the top of a phone.
      */}
      <h1 className="visually-hidden">
        <FormattedMessage id="customers.layout.title" />
      </h1>

      <MasterDetail
        className="customers__masterDetail"
        {...(arrangement === undefined ? {} : { arrangement })}
        detail={selected === null ? undefined : <Outlet />}
        detailLabel={intl.formatMessage({ id: 'customers.layout.detail' })}
        detailOpen={selected !== null}
        list={<CustomerSearchRoute />}
        listLabel={intl.formatMessage({ id: 'customers.layout.list' })}
        onCloseDetail={() => {
          void navigate({ pathname: '/customers', search })
        }}
      />
    </>
  )
}
