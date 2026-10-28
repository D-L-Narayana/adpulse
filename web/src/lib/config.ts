/**
 * Runtime configuration for the API base URL.
 *
 * Resolution order (first wins):
 *  1. `?api=https://host` on the current URL — stored once in localStorage, then stripped from the URL.
 *  2. localStorage `adpulse.apiBase` (set by 1 or from Settings).
 *  3. `VITE_API_URL` build-time env.
 *  4. Mode default: production → ngrok domain, dev → localhost:8080.
 *
 * The ngrok domain may change, so the runtime override is a first-class feature, not a debug hook.
 */
export const API_BASE_STORAGE_KEY = 'adpulse.apiBase'
export const PROD_DEFAULT_API = 'https://reassign-tableful-breeding.ngrok-free.dev'
export const DEV_DEFAULT_API = 'http://localhost:8080'

export interface ConfigEnv {
  viteApiUrl?: string
  isProd: boolean
  search?: string
  storage?: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>
}

export function normalizeBase(url: string): string {
  let u = url.trim()
  if (!u) return ''
  if (!/^https?:\/\//i.test(u)) u = `https://${u}`
  return u.replace(/\/+$/, '')
}

export function resolveApiBase(env: ConfigEnv): { base: string; source: 'query' | 'storage' | 'env' | 'default' } {
  const storage = env.storage
  if (env.search) {
    const q = new URLSearchParams(env.search).get('api')
    if (q) {
      const base = normalizeBase(q)
      if (base) {
        storage?.setItem(API_BASE_STORAGE_KEY, base)
        return { base, source: 'query' }
      }
    }
  }
  const stored = storage?.getItem(API_BASE_STORAGE_KEY)
  if (stored) return { base: normalizeBase(stored), source: 'storage' }
  if (env.viteApiUrl) return { base: normalizeBase(env.viteApiUrl), source: 'env' }
  return { base: env.isProd ? PROD_DEFAULT_API : DEV_DEFAULT_API, source: 'default' }
}

function safeStorage(): Storage | undefined {
  try {
    return typeof localStorage === 'undefined' ? undefined : localStorage
  } catch {
    return undefined
  }
}

let cached: { base: string; source: string } | null = null

export function apiBase(): string {
  if (!cached) {
    cached = resolveApiBase({
      viteApiUrl: import.meta.env.VITE_API_URL,
      isProd: import.meta.env.PROD,
      search: typeof location === 'undefined' ? '' : location.search,
      storage: safeStorage(),
    })
    // Strip ?api= from the address bar once it has been persisted.
    if (cached.source === 'query' && typeof history !== 'undefined') {
      const url = new URL(location.href)
      url.searchParams.delete('api')
      history.replaceState(history.state, '', url.toString())
    }
  }
  return cached.base
}

export function apiBaseSource(): string {
  apiBase()
  return cached?.source ?? 'default'
}

export function setApiBase(url: string) {
  const base = normalizeBase(url)
  const s = safeStorage()
  if (base) s?.setItem(API_BASE_STORAGE_KEY, base)
  else s?.removeItem(API_BASE_STORAGE_KEY)
  cached = null
}

export function resetApiBaseCache() {
  cached = null
}
