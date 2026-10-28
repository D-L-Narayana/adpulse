<script setup lang="ts">
withDefaults(defineProps<{ tone?: 'neutral' | 'ok' | 'warn' | 'bad' | 'accent' | 'info'; dot?: boolean; title?: string }>(), { tone: 'neutral', dot: true })
</script>

<template>
  <span class="badge" :class="tone" :title="title">
    <span v-if="dot" class="dot" aria-hidden="true"></span>
    <slot />
  </span>
</template>

<style scoped>
.badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 22px;
  padding: 0 8px;
  border-radius: 999px;
  font-size: var(--text-xs);
  font-weight: 500;
  line-height: 1;
  white-space: nowrap;
  border: 1px solid transparent;
  font-variant-numeric: tabular-nums;
}
.dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
  flex-shrink: 0;
}
.neutral {
  color: var(--text-muted);
  background: var(--surface-sunken);
  border-color: var(--border-soft);
}
.ok {
  color: var(--success);
  background: var(--success-soft);
}
.warn {
  color: var(--warning);
  background: var(--warning-soft);
}
.bad {
  color: var(--error);
  background: var(--error-soft);
}
.accent,
.info {
  color: var(--accent);
  background: var(--accent-soft);
}
</style>
