<script setup lang="ts">
import { useToasts } from '../../stores/toast'

const { toasts, dismiss } = useToasts()
const icon: Record<string, string> = { success: '✓', error: '!', warning: '!', info: 'i' }
</script>

<template>
  <div class="toasts" aria-live="polite" aria-atomic="false">
    <transition-group name="toast" tag="div" class="list">
      <div v-for="t in toasts" :key="t.id" class="toast" :class="t.tone" role="status">
        <span class="icon" aria-hidden="true">{{ icon[t.tone] }}</span>
        <div class="body">
          <p class="title">{{ t.title }}</p>
          <p v-if="t.detail" class="detail">{{ t.detail }}</p>
          <button v-if="t.action" type="button" class="action" @click="t.action.onClick(); dismiss(t.id)">{{ t.action.label }}</button>
        </div>
        <button type="button" class="close" aria-label="Dismiss notification" @click="dismiss(t.id)">×</button>
      </div>
    </transition-group>
  </div>
</template>

<style scoped>
.toasts {
  position: fixed;
  right: var(--s-4);
  bottom: var(--s-4);
  z-index: 60;
  width: min(380px, calc(100vw - 2 * var(--s-4)));
  pointer-events: none;
}
.list {
  display: flex;
  flex-direction: column;
  gap: var(--s-2);
}
.toast {
  pointer-events: auto;
  display: grid;
  grid-template-columns: 22px 1fr auto;
  gap: var(--s-3);
  align-items: start;
  background: #fff;
  border: 1px solid var(--border);
  border-left: 3px solid var(--accent);
  border-radius: var(--r-md);
  box-shadow: var(--shadow-md);
  padding: var(--s-3) var(--s-3) var(--s-3) var(--s-4);
}
.toast.success {
  border-left-color: var(--success);
}
.toast.error {
  border-left-color: var(--error);
}
.toast.warning {
  border-left-color: var(--warning);
}
.icon {
  width: 22px;
  height: 22px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  font-size: var(--text-xs);
  font-weight: 700;
  color: #fff;
  background: var(--accent);
}
.success .icon {
  background: var(--success);
}
.error .icon {
  background: var(--error);
}
.warning .icon {
  background: var(--warning);
}
.title {
  font-size: var(--text-sm);
  font-weight: 700;
  line-height: 1.35;
}
.detail {
  font-size: var(--text-sm);
  color: var(--text-muted);
  margin-top: 2px;
  line-height: 1.4;
  word-break: break-word;
}
.action {
  margin-top: var(--s-2);
  background: none;
  border: 0;
  padding: 0;
  color: var(--accent);
  font-weight: 500;
  font-size: var(--text-sm);
  cursor: pointer;
  text-decoration: underline;
  text-underline-offset: 2px;
}
.close {
  background: none;
  border: 0;
  color: var(--text-muted);
  font-size: 18px;
  line-height: 1;
  cursor: pointer;
  padding: 0 4px;
  border-radius: var(--r-sm);
}
.close:hover {
  color: var(--text);
}
.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
.toast-enter-active,
.toast-leave-active {
  transition: all var(--dur) var(--ease);
}
</style>
