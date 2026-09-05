import { describe, expect, it } from 'vitest'
import { failureCauseForStatus, plainLanguageDetail } from './problemDetails'

describe('failureCauseForStatus', () => {
  it('calls no answer at all a network failure', () => {
    // No status means the request never reached a server, so nothing is known about whether the
    // command ran. That is a different sentence from any server answer, and a different cause.
    expect(failureCauseForStatus(undefined)).toBe('network')
  })

  it.each([408, 504])('reads %i as a timeout', (status) => {
    expect(failureCauseForStatus(status)).toBe('timeout')
  })

  it('reads 429 as the rate-limit policy doing its job', () => {
    expect(failureCauseForStatus(429)).toBe('rateLimited')
  })

  it.each([409, 412])('reads %i as a concurrency conflict', (status) => {
    // The ETag / If-Match tokens of #53. "Somebody else changed this" is the only sentence a person
    // at a counter can act on.
    expect(failureCauseForStatus(status)).toBe('conflict')
  })

  it.each([404, 410])('reads %i as gone', (status) => {
    expect(failureCauseForStatus(status)).toBe('notFound')
  })

  it.each([500, 502, 503])('reads %i as the server’s problem, not the person’s', (status) => {
    expect(failureCauseForStatus(status)).toBe('server')
  })

  it('refuses to guess at anything else', () => {
    // A confident sentence that is wrong is worse than an honest one.
    expect(failureCauseForStatus(418)).toBe('unknown')
    expect(failureCauseForStatus(400)).toBe('unknown')
  })
})

describe('plainLanguageDetail', () => {
  it('keeps a sentence a person can read', () => {
    expect(plainLanguageDetail('This invoice was already posted at 4:31 PM.')).toBe(
      'This invoice was already posted at 4:31 PM.',
    )
  })

  it('trims it', () => {
    expect(plainLanguageDetail('  The branch is closed for the day.  ')).toBe(
      'The branch is closed for the day.',
    )
  })

  it('drops nothing at all', () => {
    expect(plainLanguageDetail(undefined)).toBeUndefined()
    expect(plainLanguageDetail('   ')).toBeUndefined()
  })

  it('drops a stack trace', () => {
    // docs/nfr/accessibility-localisation.md section 8.2: never a stack. A rule enforced by review
    // alone ships the first time a server is misconfigured, so it is enforced here instead.
    const stack =
      'Object reference not set\n   at HyFib.Billing.Post(Invoice invoice)\n   at Handler'
    expect(plainLanguageDetail(stack)).toBeUndefined()
  })

  it('drops an exception message and a namespaced type name', () => {
    expect(
      plainLanguageDetail('InvalidOperationException: sequence contains no elements'),
    ).toBeUndefined()
    expect(plainLanguageDetail('HyFib.Tailor360.Billing.PostingFailed')).toBeUndefined()
  })

  it('drops a query', () => {
    expect(plainLanguageDetail('SELECT id FROM billing.invoices WHERE id = @p0')).toBeUndefined()
  })

  it('drops anything longer than a sentence', () => {
    expect(plainLanguageDetail('word '.repeat(80))).toBeUndefined()
  })
})
