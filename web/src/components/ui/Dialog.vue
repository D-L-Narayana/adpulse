<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'

const props = withDefaults(defineProps<{ open: boolean; title: string; description?: string; danger?: boolean; width?: string }>(), { danger: false, width: '440px' })
const emit = defineEmits<{ close: [] }>()
const panel = ref<HTMLElement | null>(null)
let previouslyFocused: Element | null = null

function focusables(): HTMLElement[] {
  if (!panel.value) return []
  return Array.from(panel.value.querySelectorAll<HTMLElement>('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])')).filter((el) => !el.hasAttribute('disabled'))
}

function onKey(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    e.preventDefault()
    emit('close')
  } else if (e.key === 'Tab') {
    const els = focusables()
    if (!els.length) return
    const first = els[0]
    const last = els[els.length - 1]
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault()
      last.focus()
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault()
      first.focus()
    }
  }
}

watch(
  () => props.open,
  async (open) => {
    if (open) {
      previouslyFocused = document.activeElement
      document.addEventListener('keydown', onKey)
      document.body.style.overflow = 'hidden'
      await nextTick()
      const els = focusables()
      ;(els.find((el) => el.dataset.autofocus !== undefined) ?? els[0] ?? panel.value)?.focus()
    } else {
      document.removeEventListener('keydown', onKey)
      document.body.style.overflow = ''
      ;(previouslyFocused as HTMLElement | null)?.focus?.()
    }
  },
)
onBeforeUnmount(() => {
  document.removeEventListener('keydown', onKey)
  document.body.style.overflow = ''
})
</script>

<template>
  <teleport to="body">
    <transition name="dlg">
      <div v-if="open" class="backdrop" @mousedown.self="emit('close')">
        <div ref="panel" class="panel" :class="{ danger }" role="dialog" aria-modal="true" :aria-labelledby="'dlg-title'" :aria-describedby="description ? 'dlg-desc' : undefined" tabindex="-1" :style="{ maxWidth: width }">
          <h2 id="dlg-title" class="title">{{ title }}</h2>
          <p v-if="description" id="dlg-desc" class="desc">{{ description }}</p>
          <div class="content"><slot /></div>
          <div class="footer"><slot name="footer" /></div>
        </div>
      </div>
    </transition>
  </teleport>
</template>

<style scoped>
.backdrop {
  position: fixed;
  inset: 0;
  background: rgba(40, 37, 29, 0.42);
  display: grid;
  place-items: center;
  padding: var(--s-4);
  z-index: 50;
}
.panel {
  width: 100%;
  background: var(--surface-alt);
  border: 1px solid var(--border);
  border-radius: var(--r-lg);
  box-shadow: var(--shadow-md);
  padding: var(--s-6);
  outline: none;
}
.title {
  font-size: var(--text-lg);
}
.danger .title {
  color: var(--error);
}
.desc {
  margin-top: var(--s-2);
  color: var(--text-muted);
  font-size: var(--text-sm);
  line-height: 1.5;
}
.content:not(:empty) {
  margin-top: var(--s-4);
}
.footer {
  margin-top: var(--s-6);
  display: flex;
  justify-content: flex-end;
  gap: var(--s-2);
  flex-wrap: wrap;
}
.dlg-enter-from,
.dlg-leave-to {
  opacity: 0;
}
.dlg-enter-from .panel {
  transform: translateY(6px) scale(0.98);
}
.dlg-enter-active,
.dlg-leave-active {
  transition: opacity var(--dur) var(--ease);
}
.dlg-enter-active .panel {
  transition: transform var(--dur) var(--ease);
}
</style>
