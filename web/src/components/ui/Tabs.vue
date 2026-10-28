<script setup lang="ts">
import { useId } from 'vue'

export interface TabItem {
  id: string
  label: string
  count?: number
}
const props = defineProps<{ tabs: TabItem[]; modelValue: string; label?: string }>()
const emit = defineEmits<{ 'update:modelValue': [string] }>()
const uid = useId()

function onKey(e: KeyboardEvent, idx: number) {
  const n = props.tabs.length
  let next = idx
  if (e.key === 'ArrowRight') next = (idx + 1) % n
  else if (e.key === 'ArrowLeft') next = (idx - 1 + n) % n
  else if (e.key === 'Home') next = 0
  else if (e.key === 'End') next = n - 1
  else return
  e.preventDefault()
  emit('update:modelValue', props.tabs[next].id)
  ;(e.currentTarget as HTMLElement).parentElement?.querySelectorAll<HTMLElement>('[role=tab]')[next]?.focus()
}
</script>

<template>
  <div class="tabs" role="tablist" :aria-label="label">
    <button
      v-for="(t, i) in tabs"
      :id="`${uid}-tab-${t.id}`"
      :key="t.id"
      type="button"
      role="tab"
      class="tab"
      :class="{ active: t.id === modelValue }"
      :aria-selected="t.id === modelValue"
      :aria-controls="`${uid}-panel-${t.id}`"
      :tabindex="t.id === modelValue ? 0 : -1"
      @click="emit('update:modelValue', t.id)"
      @keydown="onKey($event, i)"
    >
      {{ t.label }}<span v-if="t.count !== undefined" class="count num">{{ t.count }}</span>
    </button>
  </div>
</template>

<style scoped>
.tabs {
  display: flex;
  gap: var(--s-1);
  border-bottom: 1px solid var(--border);
  overflow-x: auto;
  scrollbar-width: none;
}
.tab {
  position: relative;
  background: none;
  border: 0;
  padding: 10px 12px;
  font-size: var(--text-sm);
  font-weight: 500;
  color: var(--text-muted);
  cursor: pointer;
  white-space: nowrap;
  border-radius: var(--r-sm) var(--r-sm) 0 0;
}
.tab:hover {
  color: var(--text);
}
.tab.active {
  color: var(--text);
}
.tab.active::after {
  content: '';
  position: absolute;
  left: 8px;
  right: 8px;
  bottom: -1px;
  height: 2px;
  background: var(--accent);
  border-radius: 2px 2px 0 0;
}
.count {
  margin-left: 6px;
  font-size: var(--text-xs);
  padding: 1px 6px;
  border-radius: 999px;
  background: var(--surface-sunken);
  color: var(--text-muted);
}
</style>
