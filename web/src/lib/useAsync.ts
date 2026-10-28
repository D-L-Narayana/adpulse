import { ref, shallowRef, type Ref, type ShallowRef } from 'vue'

export interface AsyncState<T> {
  data: ShallowRef<T | null>
  loading: Ref<boolean>
  refreshing: Ref<boolean>
  error: Ref<unknown>
  run: (opts?: { silent?: boolean }) => Promise<T | null>
}

/**
 * Minimal async state helper. `run()` shows the loading state the first time and a silent
 * `refreshing` flag on subsequent calls so live refetches never flash skeletons.
 */
export function useAsync<T>(fn: () => Promise<T>): AsyncState<T> {
  const data = shallowRef<T | null>(null)
  const loading = ref(false)
  const refreshing = ref(false)
  const error = ref<unknown>(null)
  let seq = 0
  async function run(opts?: { silent?: boolean }) {
    const mine = ++seq
    const silent = opts?.silent ?? data.value !== null
    if (silent) refreshing.value = true
    else loading.value = true
    try {
      const result = await fn()
      if (mine !== seq) return data.value
      data.value = result
      error.value = null
      return result
    } catch (e) {
      if (mine !== seq) return data.value
      // keep stale data on silent refresh failures, surface error only on first load
      if (!silent || data.value === null) error.value = e
      return data.value
    } finally {
      if (mine === seq) {
        loading.value = false
        refreshing.value = false
      }
    }
  }
  return { data, loading, refreshing, error, run }
}

/** Resolve a promise but tolerate 404 (endpoint not implemented yet) by returning the fallback. */
export async function optional<T>(p: Promise<T>, fallback: T, tolerate: (e: unknown) => boolean): Promise<T> {
  try {
    return await p
  } catch (e) {
    if (tolerate(e)) return fallback
    throw e
  }
}
