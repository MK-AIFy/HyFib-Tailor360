import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { useIntl } from 'react-intl'
import { ApiError } from '../../auth/apiClient'
import { useCurrentUser } from '../../auth/useSession'
import { Icon } from '../../components/primitives/Icon'
import { readCustomer, registerCustomer, searchCustomers } from '../../workspace/customerApi'
import type {
  CustomerCard,
  CustomerRecord,
  DuplicateCandidate,
  RegisterCustomerInput,
} from '../../workspace/customerApi'
import './workspace.css'

function failureMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403) return 'You do not have permission for this customer action.'
    if (error.status === 409) return 'This record has changed. Review the details and try again.'
  }
  return 'The request could not be completed. Check your connection and try again.'
}

function readCandidates(error: ApiError): readonly DuplicateCandidate[] {
  const problem = error.problem as (typeof error.problem & { candidates?: unknown }) | undefined
  const candidates = problem?.candidates
  if (!Array.isArray(candidates)) return []
  return candidates.filter(
    (item): item is DuplicateCandidate =>
      typeof item === 'object' && item !== null && 'customer' in item,
  )
}

export function CustomersRoute() {
  const intl = useIntl()
  const user = useCurrentUser()
  const canRead = user.permissions.includes('customers.read')
  const canCreate = user.permissions.includes('customers.create') && user.branchId !== null
  const [term, setTerm] = useState('')
  const [submitted, setSubmitted] = useState('')
  const [searchRevision, setSearchRevision] = useState(0)
  const [cards, setCards] = useState<readonly CustomerCard[]>([])
  const [cursor, setCursor] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!canRead || submitted.length < 3) return
    const controller = new AbortController()
    void searchCustomers(submitted, undefined, controller.signal)
      .then((page) => {
        setCards(page.customers)
        setCursor(page.nextCursor)
        setError(null)
      })
      .catch((cause: unknown) => {
        if (!controller.signal.aborted) setError(failureMessage(cause))
      })
      .finally(() => {
        if (!controller.signal.aborted) setBusy(false)
      })
    return () => controller.abort()
  }, [canRead, submitted, searchRevision])

  async function loadMore() {
    if (cursor === null) return
    setBusy(true)
    try {
      const page = await searchCustomers(submitted, cursor)
      setCards((current) => [...current, ...page.customers])
      setCursor(page.nextCursor)
      setError(null)
    } catch (cause) {
      setError(failureMessage(cause))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="workspace-page">
      <div className="workspace-heading">
        <div>
          <span className="workspace-eyebrow">Customer desk</span>
          <h1>{intl.formatMessage({ id: 'navigation.destination.customers' })}</h1>
          <p>Find an existing record before starting a new intake.</p>
        </div>
        {canCreate ? (
          <Link className="workspace-button workspace-button--primary" to="/customers/new">
            <Icon name="plus" /> New customer
          </Link>
        ) : null}
      </div>

      <div className="workspace-card workspace-search">
        <form
          onSubmit={(event) => {
            event.preventDefault()
            const next = term.trim()
            if (next.length < 3 || !canRead) return
            setCards([])
            setCursor(null)
            setBusy(true)
            setSubmitted(next)
            setSearchRevision((current) => current + 1)
          }}
        >
          <label htmlFor="customer-search">Search by name, number, or phone</label>
          <div className="workspace-search__row">
            <input
              id="customer-search"
              minLength={3}
              onChange={(event) => setTerm(event.target.value)}
              placeholder="Enter at least 3 characters"
              required
              type="search"
              value={term}
            />
            <button
              className="workspace-button workspace-button--primary"
              disabled={!canRead || busy}
              type="submit"
            >
              Search
            </button>
          </div>
          <p className="workspace-hint">
            Results are scoped by your branch access. Other branches show a limited card.
          </p>
        </form>
      </div>

      {!canRead ? (
        <p className="workspace-notice">Your account cannot search customer records.</p>
      ) : null}
      {error ? (
        <p className="workspace-notice workspace-notice--error" role="alert">
          {error}
        </p>
      ) : null}
      {busy ? (
        <p className="workspace-hint" role="status">
          Looking up customers…
        </p>
      ) : null}
      {submitted && !busy && cards.length === 0 && !error ? (
        <div className="workspace-empty">
          <h2>No matching customers</h2>
          <p>
            Try a different name or number, or register a new customer after checking for
            duplicates.
          </p>
        </div>
      ) : null}
      {cards.length > 0 ? (
        <div className="workspace-results">
          <div className="workspace-section-heading">
            <h2>Matching records</h2>
            <span>{cards.length} shown</span>
          </div>
          <ul className="workspace-list">
            {cards.map((card) => (
              <li key={card.customerId}>
                <div className="workspace-customer">
                  <span className="workspace-avatar" aria-hidden="true">
                    {card.displayName.slice(0, 1).toUpperCase()}
                  </span>
                  <div>
                    <strong>{card.displayName}</strong>
                    <span>
                      {card.customerNumber} · {card.maskedPhone}
                    </span>
                  </div>
                  <span className="workspace-chip">
                    {card.visibleToCaller ? card.status : 'Other branch'}
                  </span>
                  {card.visibleToCaller ? (
                    <Link to={`/customers/${card.customerId}`}>Open record</Link>
                  ) : null}
                </div>
              </li>
            ))}
          </ul>
          {cursor ? (
            <button
              className="workspace-button"
              disabled={busy}
              onClick={() => void loadMore()}
              type="button"
            >
              Load more
            </button>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}

export function CustomerDetailRoute() {
  const { customerId } = useParams()
  const [customer, setCustomer] = useState<CustomerRecord | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!customerId) return
    const controller = new AbortController()
    void readCustomer(customerId, controller.signal)
      .then((record) => setCustomer(record))
      .catch((cause: unknown) => {
        if (!controller.signal.aborted) setError(failureMessage(cause))
      })
    return () => controller.abort()
  }, [customerId])

  return (
    <section className="workspace-page">
      <Link className="workspace-back" to="/customers">
        ← Customers
      </Link>
      {error ? (
        <p className="workspace-notice workspace-notice--error" role="alert">
          {error}
        </p>
      ) : null}
      {!customer && !error ? <p role="status">Loading customer record…</p> : null}
      {customer ? (
        <>
          <div className="workspace-heading">
            <div>
              <span className="workspace-eyebrow">{customer.customerNumber}</span>
              <h1>{customer.displayName}</h1>
              <p>
                {customer.nativeName ?? 'Customer record'} · {customer.status}
              </p>
            </div>
          </div>
          <div className="workspace-detail-grid">
            <div className="workspace-card">
              <h2>Contact</h2>
              <dl className="workspace-facts">
                <div>
                  <dt>Phone</dt>
                  <dd>{customer.phone ?? 'Not recorded or not visible'}</dd>
                </div>
                <div>
                  <dt>Email</dt>
                  <dd>{customer.email ?? 'Not recorded or not visible'}</dd>
                </div>
                <div>
                  <dt>Locality</dt>
                  <dd>{customer.locality ?? 'Not recorded or not visible'}</dd>
                </div>
                <div>
                  <dt>Language</dt>
                  <dd>{customer.language}</dd>
                </div>
              </dl>
            </div>
            <div className="workspace-card">
              <h2>Next in the journey</h2>
              <p>Review the published measurement templates before capturing a fitting.</p>
              <Link className="workspace-button" to="/measurements">
                Open measurements
              </Link>
            </div>
          </div>
        </>
      ) : null}
    </section>
  )
}

export function NewCustomerRoute() {
  const navigate = useNavigate()
  const user = useCurrentUser()
  const submission = useRef<{ fingerprint: string; key: string } | null>(null)
  const [name, setName] = useState('')
  const [nativeName, setNativeName] = useState('')
  const [phone, setPhone] = useState('')
  const [email, setEmail] = useState('')
  const [locality, setLocality] = useState('')
  const [language, setLanguage] = useState('en-IN')
  const [candidates, setCandidates] = useState<readonly DuplicateCandidate[]>([])
  const [reviewed, setReviewed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  function invalidateDuplicateReview() {
    setCandidates([])
    setReviewed(false)
  }

  if (!user.permissions.includes('customers.create')) {
    return <p className="workspace-notice">Your account cannot register customers.</p>
  }

  if (user.branchId === null) {
    return <p className="workspace-notice">Select a branch before registering a customer.</p>
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    const input: RegisterCustomerInput = {
      displayName: name.trim(),
      nativeName: nativeName.trim() || null,
      phone: phone.trim() || null,
      email: email.trim() || null,
      locality: locality.trim() || null,
      language,
      duplicatesReviewed: reviewed,
    }
    const fingerprint = JSON.stringify(input)
    if (submission.current?.fingerprint !== fingerprint) {
      submission.current = { fingerprint, key: crypto.randomUUID() }
    }
    try {
      const created = await registerCustomer(input, submission.current.key)
      void navigate(`/customers/${created.customerId}`)
    } catch (cause) {
      if (cause instanceof ApiError && cause.code === 'customers.duplicates-not-reviewed') {
        setCandidates(readCandidates(cause))
        setReviewed(false)
      } else {
        setError(failureMessage(cause))
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="workspace-page">
      <Link className="workspace-back" to="/customers">
        ← Customers
      </Link>
      <div className="workspace-heading">
        <div>
          <span className="workspace-eyebrow">Customer desk</span>
          <h1>New customer</h1>
          <p>Start with the details needed to identify the person at the counter.</p>
        </div>
      </div>
      <form className="workspace-card workspace-form" onSubmit={(event) => void submit(event)}>
        <div className="workspace-form__grid">
          <label>
            Full name{' '}
            <input
              autoComplete="name"
              onChange={(event) => {
                setName(event.target.value)
                invalidateDuplicateReview()
              }}
              required
              value={name}
            />
          </label>
          <label>
            Name in local script{' '}
            <input
              onChange={(event) => {
                setNativeName(event.target.value)
                invalidateDuplicateReview()
              }}
              value={nativeName}
            />
          </label>
          <label>
            Phone{' '}
            <input
              autoComplete="tel"
              onChange={(event) => {
                setPhone(event.target.value)
                invalidateDuplicateReview()
              }}
              type="tel"
              value={phone}
            />
          </label>
          <label>
            Email{' '}
            <input
              autoComplete="email"
              onChange={(event) => {
                setEmail(event.target.value)
                invalidateDuplicateReview()
              }}
              type="email"
              value={email}
            />
          </label>
          <label>
            Locality{' '}
            <input
              onChange={(event) => {
                setLocality(event.target.value)
                invalidateDuplicateReview()
              }}
              value={locality}
            />
          </label>
          <label>
            Preferred language{' '}
            <select
              onChange={(event) => {
                setLanguage(event.target.value)
                invalidateDuplicateReview()
              }}
              value={language}
            >
              <option value="en-IN">English</option>
              <option value="ta-IN">Tamil</option>
            </select>
          </label>
        </div>
        {candidates.length > 0 ? (
          <div className="workspace-duplicates" role="alert">
            <h2>Check possible matches</h2>
            <p>Read these records before confirming this is a new customer.</p>
            <ul>
              {candidates.map((candidate) => (
                <li key={candidate.customer.customerId}>
                  <strong>{candidate.customer.displayName}</strong> ·{' '}
                  {candidate.customer.customerNumber} · {candidate.customer.maskedPhone}{' '}
                  <span>({candidate.confidence} match)</span>
                </li>
              ))}
            </ul>
            <label className="workspace-checkbox">
              <input
                checked={reviewed}
                onChange={(event) => setReviewed(event.target.checked)}
                type="checkbox"
              />{' '}
              I checked these matches and this is a different person.
            </label>
          </div>
        ) : null}
        {error ? (
          <p className="workspace-notice workspace-notice--error" role="alert">
            {error}
          </p>
        ) : null}
        <div className="workspace-form__actions">
          <Link className="workspace-button" to="/customers">
            Cancel
          </Link>
          <button
            className="workspace-button workspace-button--primary"
            disabled={busy || (candidates.length > 0 && !reviewed)}
            type="submit"
          >
            {busy ? 'Saving…' : 'Register customer'}
          </button>
        </div>
      </form>
    </section>
  )
}
