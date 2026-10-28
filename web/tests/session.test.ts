import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { _resetSession, forgetKey, keyFor, knownTenants, lastSlug, loadSession, readKeys, rememberKey, signOut, useSession } from '../src/stores/session'
import { resetApiBaseCache } from '../src/lib/config'

function mockFetch(handler: (url: string, init?: RequestInit) => Response | Promise<Response>) {
  const f = vi.fn((url: string | URL | Request, init?: RequestInit) => Promise.resolve(handler(String(url), init)))
  vi.stubGlobal('fetch', f)
  return f
}
const json = (body: unknown, status = 200, type = 'application/json') => new Response(JSON.stringify(body), { status, headers: { 'content-type': type } })

describe('key storage', () => {
  beforeEach(() => {
    localStorage.clear()
    _resetSession()
    resetApiBaseCache()
  })
  it('remembers one key per slug and tracks the last opened tenant', () => {
    rememberKey('acme', 'k-acme')
    rememberKey('globex', 'k-globex')
    expect(keyFor('acme')).toBe('k-acme')
    expect(knownTenants().sort()).toEqual(['acme', 'globex'])
    expect(lastSlug()).toBe('globex')
    forgetKey('globex')
    expect(keyFor('globex')).toBeNull()
    expect(lastSlug()).toBeNull()
  })
  it('survives corrupted storage', () => {
    localStorage.setItem('adpulse.keys', '{not json')
    expect(readKeys()).toEqual({})
  })
})

describe('loadSession', () => {
  beforeEach(() => {
    localStorage.clear()
    _resetSession()
    resetApiBaseCache()
  })
  afterEach(() => vi.unstubAllGlobals())

  it('reports no-key when the browser has no key for the slug', async () => {
    mockFetch(() => json({}))
    expect(await loadSession('acme')).toBe('no-key')
  })

  it('persists the ?key= from a magic link, calls /tenants/me with the right headers and becomes ready', async () => {
    const f = mockFetch(() => json({ id: 't1', slug: 'acme', name: 'Acme', email: 'a@b.co', currency: 'INR' }))
    const status = await loadSession('acme', 'magic-key')
    expect(status).toBe('ready')
    expect(keyFor('acme')).toBe('magic-key')
    const [url, init] = f.mock.calls[0]
    expect(String(url)).toMatch(/\/api\/v1\/tenants\/me$/)
    const h = new Headers(init?.headers)
    expect(h.get('X-Api-Key')).toBe('magic-key')
    expect(h.get('ngrok-skip-browser-warning')).toBe('1')
    const s = useSession()
    expect(s.tenant.value?.name).toBe('Acme')
    expect(s.isReady.value).toBe(true)
  })

  it('maps 401 problem+json to invalid-key and keeps the message', async () => {
    rememberKey('acme', 'stale')
    mockFetch(() => json({ type: 'https://adpulse.dev/errors/unknown_api_key', title: 'Unauthorized', status: 401, detail: 'API key is not recognised.' }, 401, 'application/problem+json'))
    expect(await loadSession('acme')).toBe('invalid-key')
    const s = useSession()
    expect(s.state.error?.message).toBe('API key is not recognised.')
    expect(s.state.error?.code).toBe('unknown_api_key')
  })

  it('treats 404 from /tenants/me as an invalid key too', async () => {
    rememberKey('acme', 'x')
    mockFetch(() => json({ status: 404, title: 'Not Found' }, 404, 'application/problem+json'))
    expect(await loadSession('acme')).toBe('invalid-key')
  })

  it('detects a key that belongs to another tenant', async () => {
    rememberKey('acme', 'k')
    mockFetch(() => json({ slug: 'globex', name: 'Globex' }))
    expect(await loadSession('acme')).toBe('wrong-tenant')
    expect(useSession().tenant.value?.slug).toBe('globex')
  })

  it('reports unreachable on network failure and exposes the URL that was tried', async () => {
    rememberKey('acme', 'k')
    vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))))
    expect(await loadSession('acme')).toBe('unreachable')
    expect(useSession().state.error?.url).toContain('/api/v1/tenants/me')
  })

  it('signOut forgets the key and returns to no-key', async () => {
    mockFetch(() => json({ slug: 'acme', name: 'Acme' }))
    await loadSession('acme', 'k')
    signOut('acme')
    expect(keyFor('acme')).toBeNull()
    expect(useSession().status.value).toBe('no-key')
  })
})
