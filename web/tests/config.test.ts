import { describe, expect, it } from 'vitest'
import { DEV_DEFAULT_API, PROD_DEFAULT_API, normalizeBase, resolveApiBase, API_BASE_STORAGE_KEY } from '../src/lib/config'

function memStorage() {
  const m = new Map<string, string>()
  return { getItem: (k: string) => m.get(k) ?? null, setItem: (k: string, v: string) => void m.set(k, v), removeItem: (k: string) => void m.delete(k), map: m }
}

describe('API base resolution', () => {
  it('uses production default when nothing is configured', () => {
    expect(resolveApiBase({ isProd: true })).toEqual({ base: PROD_DEFAULT_API, source: 'default' })
    expect(resolveApiBase({ isProd: false })).toEqual({ base: DEV_DEFAULT_API, source: 'default' })
  })
  it('prefers VITE_API_URL over the default and strips trailing slashes', () => {
    expect(resolveApiBase({ isProd: true, viteApiUrl: 'https://api.example.com/' })).toEqual({ base: 'https://api.example.com', source: 'env' })
  })
  it('persists ?api= once and prefers it afterwards', () => {
    const storage = memStorage()
    const first = resolveApiBase({ isProd: true, viteApiUrl: 'https://old.example.com', search: '?api=https://new.ngrok-free.app/&x=1', storage })
    expect(first).toEqual({ base: 'https://new.ngrok-free.app', source: 'query' })
    expect(storage.map.get(API_BASE_STORAGE_KEY)).toBe('https://new.ngrok-free.app')
    const second = resolveApiBase({ isProd: true, viteApiUrl: 'https://old.example.com', search: '', storage })
    expect(second).toEqual({ base: 'https://new.ngrok-free.app', source: 'storage' })
  })
  it('adds https:// when the scheme is missing', () => {
    expect(normalizeBase('adpulse-api-wbw0.onrender.com')).toBe('https://adpulse-api-wbw0.onrender.com')
    expect(normalizeBase('http://localhost:8080/')).toBe('http://localhost:8080')
    expect(normalizeBase('   ')).toBe('')
  })
})
