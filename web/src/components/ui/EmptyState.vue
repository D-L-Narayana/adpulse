<script setup lang="ts">
withDefaults(defineProps<{ title: string; description?: string; tone?: 'neutral' | 'error' | 'warning'; compact?: boolean }>(), { tone: 'neutral', compact: false })
</script>

<template>
  <div class="empty" :class="[tone, { compact }]" role="status">
    <div class="mark" aria-hidden="true"><slot name="mark"><span class="bar"></span><span class="bar short"></span></slot></div>
    <h3 class="title">{{ title }}</h3>
    <p v-if="description || $slots.description" class="desc"><slot name="description">{{ description }}</slot></p>
    <div v-if="$slots.default" class="actions"><slot /></div>
    <div v-if="$slots.footer" class="footer"><slot name="footer" /></div>
  </div>
</template>

<style scoped>
.empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  padding: var(--s-12) var(--s-6);
  gap: var(--s-2);
  border: 1px dashed var(--border);
  border-radius: var(--r-lg);
  background: var(--surface-alt);
}
.empty.compact {
  padding: var(--s-8) var(--s-4);
}
.mark {
  display: flex;
  flex-direction: column;
  gap: 5px;
  margin-bottom: var(--s-3);
}
.bar {
  display: block;
  width: 44px;
  height: 6px;
  border-radius: 3px;
  background: var(--accent-soft);
}
.bar.short {
  width: 28px;
  background: var(--accent);
  opacity: 0.6;
}
.error .bar {
  background: var(--error-soft);
}
.error .bar.short {
  background: var(--error);
}
.warning .bar {
  background: var(--warning-soft);
}
.warning .bar.short {
  background: var(--warning);
}
.title {
  font-size: var(--text-lg);
}
.desc {
  color: var(--text-muted);
  font-size: var(--text-sm);
  max-width: 44ch;
  line-height: 1.5;
}
.actions {
  display: flex;
  gap: var(--s-2);
  flex-wrap: wrap;
  justify-content: center;
  margin-top: var(--s-3);
}
.footer {
  margin-top: var(--s-3);
  font-size: var(--text-xs);
  color: var(--text-muted);
  max-width: 56ch;
  word-break: break-all;
}
</style>
