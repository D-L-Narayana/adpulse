import { computed, reactive, readonly } from 'vue'
import { api, ApiError } from '../lib/api'
import { setCurrency } from '../lib/format'
import type { Tenant } from '../lib/types'

export const KEYS_STORAGE = 'adpulse.keys' // { [slug]: apiKey }
export const LAST_SLUG_STORAGE = 'adpulse.lastSlug'

export type SessionStatus = 'idle' | 'loading' | 'ready' | 'no-key' | 'invalid-key' | 'wrong-tenant' | 'unreachable' | 'error'

interface SessionState {
  slug: string | null
  key: string | null
  tenant: Tenant | null
  status: SessionStatus
  error: ApiError | null
}

function storage(): Storage | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage
  } catch {
    return null
  }
}

export function readKeys(): Record<string, string> {
  try {
    const raw = storage()?.getItem(KEYS_STORAGE)
    const parsed = raw ? JSON.parse(raw) : {}
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, string>) : {}
  } catch {
    return {}
  }
}

export function writeKeys(keys: Record<string, string>) {
  storage()?.setItem(KEYS_STORAGE, JSON.stringify(keys))
}

export function rememberKey(slug: string, key: string) {
  const keys = readKeys()
  keys[slug] = key
  writeKeys(keys)
  storage()?.setItem(LAST_SLUG_STORAGE, slug)
}

export function forgetKey(slug: string) {
  const keys = readKeys()
  delete keys[slug]
  writeKeys(keys)
  if (storage()?.getItem(LAST_SLUG_STORAGE) === slug) storage()?.removeItem(LAST_SLUG_STORAGE)
}

export function keyFor(slug: string): string | null {
  return readKeys()[slug] ?? null
}

export function lastSlug(): string | null {
  return storage()?.getItem(LAST_SLUG_STORAGE) ?? null
}

export function knownTenants(): string[] {
  return Object.keys(readKeys())
}

const state = reactive<SessionState>({ slug: null, key: null, tenant: null, status: 'idle', error: null })
let loadSeq = 0

/**
 * Load the session for a route slug. `queryKey` is the `?key=` from a magic URL — it is persisted and the
 * caller is expected to strip it from the address bar.
 */
export async function loadSession(slug: string, queryKey?: string | null): Promise<SessionStatus> {
  const seq = ++loadSeq
  if (queryKey) rememberKey(slug, queryKey)
  const key = keyFor(slug)
  state.slug = slug
  state.key = key
  state.error = null
  if (!key) {
    state.tenant = null
    state.status = 'no-key'
    return state.status
  }
  state.status = 'loading'
  try {
    const me = await api.me(key)
    if (seq !== loadSeq) return state.status
    // When the API tells us a slug, make sure the key really belongs to the tenant in the URL.
    if (me.slug && me.slug !== slug) {
      state.tenant = me
      state.status = 'wrong-tenant'
      return state.status
    }
    state.tenant = { ...me, slug: me.slug || slug }
    setCurrency(me.currency)
    storage()?.setItem(LAST_SLUG_STORAGE, slug)
    state.status = 'ready'
  } catch (e) {
    if (seq !== loadSeq) return state.status
    const err = e instanceof ApiError ? e : new ApiError('http', String(e), { url: '' })
    state.error = err
    state.tenant = null
    if (err.kind === 'unauthorized' || err.kind === 'not-found') state.status = 'invalid-key'
    else if (err.isUnreachable) state.status = 'unreachable'
    else state.status = 'error'
  }
  return state.status
}

export function signOut(slug: string) {
  forgetKey(slug)
  if (state.slug === slug) {
    state.key = null
    state.tenant = null
    state.status = 'no-key'
  }
}

export function useSession() {
  return {
    state: readonly(state),
    key: computed(() => state.key),
    tenant: computed(() => state.tenant),
    slug: computed(() => state.slug),
    status: computed(() => state.status),
    isReady: computed(() => state.status === 'ready'),
    load: loadSession,
    signOut,
  }
}

/** Test helper. */
export function _resetSession() {
  state.slug = null
  state.key = null
  state.tenant = null
  state.status = 'idle'
  state.error = null
}
