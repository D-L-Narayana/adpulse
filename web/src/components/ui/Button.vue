<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'ghost' | 'danger'
    size?: 'sm' | 'md' | 'lg'
    type?: 'button' | 'submit'
    loading?: boolean
    disabled?: boolean
    block?: boolean
    to?: string
  }>(),
  { variant: 'secondary', size: 'md', type: 'button', loading: false, disabled: false, block: false },
)
const isDisabled = computed(() => props.disabled || props.loading)
</script>

<template>
  <router-link v-if="to" :to="to" class="btn" :class="[variant, size, { block }]"><slot /></router-link>
  <button v-else :type="type" class="btn" :class="[variant, size, { block, loading }]" :disabled="isDisabled" :aria-busy="loading || undefined">
    <span v-if="loading" class="spinner" aria-hidden="true"></span>
    <span class="label"><slot /></span>
  </button>
</template>

<style scoped>
.btn {
  --h: 36px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--s-2);
  height: var(--h);
  padding: 0 14px;
  border-radius: var(--r-md);
  border: 1px solid transparent;
  font-weight: 500;
  font-size: var(--text-sm);
  line-height: 1;
  cursor: pointer;
  text-decoration: none;
  white-space: nowrap;
  transition: background var(--dur-fast) var(--ease), border-color var(--dur-fast) var(--ease), color var(--dur-fast) var(--ease);
  user-select: none;
}
.btn.sm {
  --h: 30px;
  padding: 0 10px;
  font-size: var(--text-xs);
}
.btn.lg {
  --h: 44px;
  padding: 0 20px;
  font-size: var(--text-md);
}
.btn.block {
  width: 100%;
}
.btn:disabled {
  cursor: not-allowed;
  opacity: 0.55;
}
.primary {
  background: var(--accent);
  color: var(--text-on-accent);
  border-color: var(--accent);
}
.primary:not(:disabled):hover {
  background: var(--accent-hover);
  border-color: var(--accent-hover);
}
.secondary {
  background: var(--surface-alt);
  color: var(--text);
  border-color: var(--border);
}
.secondary:not(:disabled):hover {
  border-color: var(--text-muted);
  background: #fff;
}
.ghost {
  background: transparent;
  color: var(--text);
  border-color: transparent;
}
.ghost:not(:disabled):hover {
  background: var(--surface-sunken);
}
.danger {
  background: transparent;
  color: var(--error);
  border-color: var(--error);
}
.danger:not(:disabled):hover {
  background: var(--error);
  color: #fff;
}
.spinner {
  width: 14px;
  height: 14px;
  border-radius: 50%;
  border: 2px solid currentColor;
  border-right-color: transparent;
  animation: spin 0.8s linear infinite;
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
@media (prefers-reduced-motion: reduce) {
  .spinner {
    animation: none;
    border-right-color: currentColor;
    opacity: 0.6;
  }
}
</style>
