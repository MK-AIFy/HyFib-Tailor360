import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useIntl } from 'react-intl'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { ApiError } from '../../auth/apiClient'
import type { VersionedResponse } from '../../auth/apiClient'
import { useCurrentUser } from '../../auth/useSession'
import { readCurrentCatalog } from '../../catalog/catalogApi'
import type { OrderableCatalog } from '../../catalog/types'
import {
  addOrderDraftGarment,
  listRecentOrderDrafts,
  readOrderDraft,
  startOrderDraft,
} from '../../workspace/orderApi'
import type { OrderDraft, RecentOrderDrafts } from '../../workspace/orderApi'
import './workspace.css'

function requestMessage(error: unknown, defaultMessage: string, conflictMessage: string): string {
  return error instanceof ApiError && error.status === 409 ? conflictMessage : defaultMessage
}

export function OrdersRoute() {
  const intl = useIntl()
  const user = useCurrentUser()
  const canIntake = user.permissions.includes('orders.intake') && user.branchId !== null
  const [recent, setRecent] = useState<RecentOrderDrafts | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!canIntake) return
    const controller = new AbortController()
    void listRecentOrderDrafts(controller.signal)
      .then((drafts) => {
        setRecent(drafts)
        setError(null)
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setError(intl.formatMessage({ id: 'orderIntake.error' }))
        }
      })
    return () => controller.abort()
  }, [canIntake, intl])

  return (
    <section className="workspace-page">
      <div className="workspace-heading">
        <div>
          <span className="workspace-eyebrow">
            {intl.formatMessage({ id: 'orderIntake.unpriced' })}
          </span>
          <h1>{intl.formatMessage({ id: 'orderIntake.title' })}</h1>
          <p>{intl.formatMessage({ id: 'orderIntake.intro' })}</p>
        </div>
      </div>
      <div className="workspace-card workspace-card--accent">
        <h2>{intl.formatMessage({ id: 'orderIntake.unpriced' })}</h2>
        <p>{intl.formatMessage({ id: 'orderIntake.unpricedBody' })}</p>
        {canIntake ? (
          <Link className="workspace-button workspace-button--primary" to="/customers">
            {intl.formatMessage({ id: 'orderIntake.findCustomer' })}
          </Link>
        ) : (
          <p className="workspace-notice">{intl.formatMessage({ id: 'orderIntake.noAccess' })}</p>
        )}
      </div>
      {canIntake ? (
        <div className="workspace-card">
          <h2>{intl.formatMessage({ id: 'orderIntake.recentTitle' })}</h2>
          <p>{intl.formatMessage({ id: 'orderIntake.recentBody' })}</p>
          {error ? (
            <p className="workspace-notice workspace-notice--error" role="alert">
              {error}
            </p>
          ) : null}
          {!recent && !error ? (
            <p role="status">{intl.formatMessage({ id: 'orderIntake.recentLoading' })}</p>
          ) : null}
          {recent && recent.drafts.length === 0 ? (
            <p>{intl.formatMessage({ id: 'orderIntake.recentEmpty' })}</p>
          ) : null}
          {recent && recent.drafts.length > 0 ? (
            <ul className="workspace-list workspace-order-drafts">
              {recent.drafts.map((draft) => (
                <li key={draft.draftId}>
                  <Link to={`/orders/drafts/${draft.draftId}`}>
                    <strong>{draft.customerName}</strong>
                    <span>{draft.customerNumber}</span>
                    <small>
                      {intl.formatMessage(
                        { id: 'orderIntake.recentGarments' },
                        { count: draft.garmentCount },
                      )}
                      {' · '}
                      {intl.formatMessage(
                        { id: 'orderIntake.updated' },
                        {
                          date: intl.formatDate(new Date(draft.updatedAt), { dateStyle: 'medium' }),
                        },
                      )}
                    </small>
                  </Link>
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}

export function NewOrderDraftRoute() {
  const intl = useIntl()
  const navigate = useNavigate()
  const user = useCurrentUser()
  const [params] = useSearchParams()
  const customerId = params.get('customerId')
  const requestKey = useRef(crypto.randomUUID())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const canIntake = user.permissions.includes('orders.intake') && user.branchId !== null

  async function start(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canIntake || !customerId) return
    setBusy(true)
    setError(null)
    try {
      const created = await startOrderDraft(customerId, requestKey.current)
      void navigate(`/orders/drafts/${created.value.draftId}`)
    } catch (cause) {
      setError(
        requestMessage(
          cause,
          intl.formatMessage({ id: 'orderIntake.error' }),
          intl.formatMessage({ id: 'orderIntake.conflict' }),
        ),
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="workspace-page">
      <Link className="workspace-back" to="/customers">
        ← {intl.formatMessage({ id: 'orderIntake.findCustomer' })}
      </Link>
      <div className="workspace-heading">
        <div>
          <span className="workspace-eyebrow">
            {intl.formatMessage({ id: 'orderIntake.unpriced' })}
          </span>
          <h1>{intl.formatMessage({ id: 'orderIntake.newTitle' })}</h1>
          <p>{intl.formatMessage({ id: 'orderIntake.newBody' })}</p>
        </div>
      </div>
      {!customerId ? (
        <p className="workspace-notice">
          {intl.formatMessage({ id: 'orderIntake.customerMissing' })}
        </p>
      ) : null}
      {!canIntake ? (
        <p className="workspace-notice">{intl.formatMessage({ id: 'orderIntake.noAccess' })}</p>
      ) : null}
      {error ? (
        <p className="workspace-notice workspace-notice--error" role="alert">
          {error}
        </p>
      ) : null}
      {customerId && canIntake ? (
        <form className="workspace-card" onSubmit={(event) => void start(event)}>
          <button
            className="workspace-button workspace-button--primary"
            disabled={busy}
            type="submit"
          >
            {intl.formatMessage({ id: busy ? 'orderIntake.starting' : 'orderIntake.start' })}
          </button>
        </form>
      ) : null}
    </section>
  )
}

export function OrderDraftRoute() {
  const intl = useIntl()
  const { draftId } = useParams()
  const [record, setRecord] = useState<VersionedResponse<OrderDraft> | null>(null)
  const [catalog, setCatalog] = useState<OrderableCatalog | null>(null)
  const [serviceTypeId, setServiceTypeId] = useState('')
  const [quantity, setQuantity] = useState(1)
  const [notes, setNotes] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const pending = useRef<{ fingerprint: string; key: string } | null>(null)

  useEffect(() => {
    if (!draftId) return
    const controller = new AbortController()
    void Promise.all([
      readOrderDraft(draftId, controller.signal),
      readCurrentCatalog(controller.signal),
    ])
      .then(([nextRecord, nextCatalog]) => {
        setRecord(nextRecord)
        setCatalog(nextCatalog)
        setError(null)
      })
      .catch((cause: unknown) => {
        if (!controller.signal.aborted) {
          setError(
            requestMessage(
              cause,
              intl.formatMessage({ id: 'orderIntake.error' }),
              intl.formatMessage({ id: 'orderIntake.conflict' }),
            ),
          )
        }
      })
    return () => controller.abort()
  }, [draftId, intl])

  async function reload() {
    if (!draftId) return
    setBusy(true)
    try {
      setRecord(await readOrderDraft(draftId))
      setError(null)
    } catch {
      setError(intl.formatMessage({ id: 'orderIntake.error' }))
    } finally {
      setBusy(false)
    }
  }

  async function add(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!draftId || !record?.version || !serviceTypeId || busy) return
    const input = {
      draftId,
      serviceTypeId,
      quantity,
      notes: notes.trim() || null,
      version: record.version,
    }
    const fingerprint = JSON.stringify(input)
    if (pending.current?.fingerprint !== fingerprint) {
      pending.current = { fingerprint, key: crypto.randomUUID() }
    }
    setBusy(true)
    setError(null)
    try {
      const next = await addOrderDraftGarment({ ...input, idempotencyKey: pending.current.key })
      setRecord(next)
      setServiceTypeId('')
      setQuantity(1)
      setNotes('')
      pending.current = null
    } catch (cause) {
      setError(
        requestMessage(
          cause,
          intl.formatMessage({ id: 'orderIntake.error' }),
          intl.formatMessage({ id: 'orderIntake.conflict' }),
        ),
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="workspace-page">
      <Link className="workspace-back" to="/orders">
        ← {intl.formatMessage({ id: 'orderIntake.title' })}
      </Link>
      <div className="workspace-heading">
        <div>
          <span className="workspace-eyebrow">
            {intl.formatMessage({ id: 'orderIntake.unpriced' })}
          </span>
          <h1>
            {record?.value.customerName ?? intl.formatMessage({ id: 'orderIntake.draftTitle' })}
          </h1>
          {record ? <p>{record.value.customerNumber}</p> : null}
        </div>
      </div>
      {!record && !error ? (
        <p role="status">{intl.formatMessage({ id: 'orderIntake.draftLoading' })}</p>
      ) : null}
      {error ? (
        <div className="workspace-notice workspace-notice--error" role="alert">
          <p>{error}</p>
          {record ? (
            <button
              className="workspace-button"
              disabled={busy}
              onClick={() => void reload()}
              type="button"
            >
              {intl.formatMessage({ id: 'orderIntake.reload' })}
            </button>
          ) : null}
        </div>
      ) : null}
      {record ? (
        <div className="workspace-detail-grid">
          <div className="workspace-card">
            <h2>{intl.formatMessage({ id: 'orderIntake.garments' })}</h2>
            {record.value.garments.length === 0 ? (
              <p>{intl.formatMessage({ id: 'orderIntake.empty' })}</p>
            ) : (
              <ul className="workspace-list">
                {record.value.garments.map((garment) => (
                  <li key={garment.garmentId}>
                    <strong>{garment.serviceName}</strong> ·{' '}
                    {intl.formatMessage({ id: 'orderIntake.pieces' }, { count: garment.quantity })}
                    {garment.notes ? <p>{garment.notes}</p> : null}
                  </li>
                ))}
              </ul>
            )}
          </div>
          <form className="workspace-card workspace-form" onSubmit={(event) => void add(event)}>
            <h2>{intl.formatMessage({ id: 'orderIntake.addGarment' })}</h2>
            {!catalog ? (
              <p role="status">{intl.formatMessage({ id: 'orderIntake.catalogLoading' })}</p>
            ) : null}
            {catalog && catalog.services.length === 0 ? (
              <p>{intl.formatMessage({ id: 'orderIntake.noCatalog' })}</p>
            ) : null}
            <label>
              {intl.formatMessage({ id: 'orderIntake.service' })}
              <select
                onChange={(event) => setServiceTypeId(event.target.value)}
                required
                value={serviceTypeId}
              >
                <option value="">{intl.formatMessage({ id: 'orderIntake.chooseService' })}</option>
                {catalog?.services.map((service) => (
                  <option key={service.serviceTypeId} value={service.serviceTypeId}>
                    {service.categoryName} · {service.serviceName}
                  </option>
                ))}
              </select>
            </label>
            <label>
              {intl.formatMessage({ id: 'orderIntake.quantity' })}
              <input
                min={1}
                max={100}
                onChange={(event) => setQuantity(Number(event.target.value))}
                required
                type="number"
                value={quantity}
              />
            </label>
            <label>
              {intl.formatMessage({ id: 'orderIntake.notes' })}
              <textarea
                maxLength={1000}
                onChange={(event) => setNotes(event.target.value)}
                placeholder={intl.formatMessage({ id: 'orderIntake.notesHint' })}
                value={notes}
              />
            </label>
            <button
              className="workspace-button workspace-button--primary"
              disabled={busy || !record.version || !catalog?.services.length}
              type="submit"
            >
              {intl.formatMessage({ id: busy ? 'orderIntake.saving' : 'orderIntake.add' })}
            </button>
          </form>
        </div>
      ) : null}
    </section>
  )
}
