import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { api, ApiError, normalizeKpiRow, normalizePacingStatus, normalizeSummary, pick } from '../src/lib/api'
import { resetApiBaseCache } from '../src/lib/config'

const json = (body: unknown, status = 200, type = 'application/json') => new Response(JSON.stringify(body), { status, headers: { 'content-type': type } })

describe('normalisers', () => {
  it('pick tolerates camelCase, PascalCase and snake_case', () => {
    expect(pick({ pacingPercent: 1 }, 'pacingPercent')).toBe(1)
    expect(pick({ PacingPercent: 2 }, 'pacingPercent')).toBe(2)
    expect(pick({ pacing_percent: 3 }, 'pacingPercent')).toBe(3)
    expect(pick(undefined, 'x')).toBeUndefined()
  })
  it('normalises pacing status spellings', () => {
    expect(normalizePacingStatus('on-track')).toBe('on-track')
    expect(normalizePacingStatus('OnTrack')).toBe('on-track')
    expect(normalizePacingStatus('Over')).toBe('over')
    expect(normalizePacingStatus('under_pacing')).toBe('under')
    expect(normalizePacingStatus(undefined)).toBe('not-started')
  })
  it('derives CTR/CPC/CPA when the API omits them', () => {
    const r = normalizeKpiRow({ channel: 'Meta', spend: 100, clicks: 50, impressions: 1000, conversions: 4 })
    expect(r.ctr).toBe(5)
    expect(r.cpc).toBe(2)
    expect(r.cpa).toBe(25)
  })
  it('reads nested pacing blocks on campaign summaries', () => {
    const s = normalizeSummary({ id: 'a', name: 'A', channel: 'GoogleAds', status: 'Active', dailyBudget: 10, startDate: '2025-01-01', endDate: '2025-01-31', spend: 1, pacing: { status: 'over', pacingPercent: 130, expectedSpendToDate: 10, actualSpendToDate: 13 } })
    expect(s.pacingStatus).toBe('over')
    expect(s.pacingPercent).toBe(130)
    expect(s.expectedSpendToDate).toBe(10)
  })
})

describe('request()', () => {
  beforeEach(() => resetApiBaseCache())
  afterEach(() => vi.unstubAllGlobals())

  it('creates a tenant and returns slug + key shown once', async () => {
    const f = vi.fn(async (_u: string, init?: RequestInit) => {
      expect(init?.method).toBe('POST')
      expect(JSON.parse(String(init?.body))).toEqual({ name: 'Acme', email: 'a@b.co' })
      return json({ slug: 'acme', apiKey: 'adp_secret', name: 'Acme' }, 201)
    })
    vi.stubGlobal('fetch', f)
    const t = await api.createTenant('Acme', 'a@b.co')
    expect(t).toMatchObject({ slug: 'acme', apiKey: 'adp_secret', name: 'Acme', email: 'a@b.co' })
    expect(String(f.mock.calls[0][0])).toMatch(/\/api\/v1\/tenants$/)
  })

  it('turns validation problems into readable messages', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ status: 422, title: 'Validation failed', errors: { Email: ['must be a valid email'] } }, 422, 'application/problem+json')))
    await expect(api.createTenant('A', 'nope')).rejects.toMatchObject({ kind: 'validation', message: 'Email: must be a valid email' })
  })

  it('recognises HTML (ngrok offline page) instead of JSON', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('<html>ngrok</html>', { status: 404, headers: { 'content-type': 'text/html' } })))
    const e = await api.me('k').catch((x) => x as ApiError)
    expect(e).toBeInstanceOf(ApiError)
    expect((e as ApiError).message).toMatch(/HTML page/)
  })

  it('tolerates list envelopes and bare arrays', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ items: [{ id: '1', name: 'X', channel: 'Meta', status: 'Active', dailyBudget: 5, startDate: '2025-01-01', endDate: '2025-01-02' }] })))
    const rows = await api.campaigns('k')
    expect(rows).toHaveLength(1)
    expect(rows[0].name).toBe('X')
  })
})
