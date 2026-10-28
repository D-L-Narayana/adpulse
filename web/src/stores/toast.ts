import { reactive, readonly } from 'vue'

export type ToastTone = 'info' | 'success' | 'error' | 'warning'
export interface Toast {
  id: number
  tone: ToastTone
  title: string
  detail?: string
  timeout: number
  action?: { label: string; onClick: () => void }
}

const state = reactive<{ items: Toast[] }>({ items: [] })
let nextId = 1

export function toast(input: Omit<Toast, 'id' | 'timeout'> & { timeout?: number }): number {
  const id = nextId++
  const t: Toast = { id, timeout: input.timeout ?? (input.tone === 'error' ? 9000 : 5000), ...input }
  state.items.push(t)
  if (state.items.length > 4) state.items.shift()
  if (t.timeout > 0) setTimeout(() => dismissToast(id), t.timeout)
  return id
}

export function dismissToast(id: number) {
  const i = state.items.findIndex((t) => t.id === id)
  if (i !== -1) state.items.splice(i, 1)
}

export function useToasts() {
  return { toasts: readonly(state).items, dismiss: dismissToast, push: toast }
}

export const notify = {
  success: (title: string, detail?: string) => toast({ tone: 'success', title, detail }),
  error: (title: string, detail?: string) => toast({ tone: 'error', title, detail }),
  info: (title: string, detail?: string) => toast({ tone: 'info', title, detail }),
  warning: (title: string, detail?: string) => toast({ tone: 'warning', title, detail }),
}
