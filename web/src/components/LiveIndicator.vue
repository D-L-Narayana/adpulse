<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { fmtAgo } from '../lib/format'
import { useLive } from '../stores/live'

const live = useLive()
const now = ref(Date.now())
let t: ReturnType<typeof setInterval> | null = null
onMounted(() => (t = setInterval(() => (now.value = Date.now()), 1000)))
onBeforeUnmount(() => t && clearInterval(t))

const state = computed<'live' | 'polling' | 'connecting' | 'off'>(() => {
  if (live.mode === 'sse' && live.sse === 'open') return 'live'
  if (live.mode === 'polling') return 'polling'
  if (live.sse === 'connecting' || live.sse === 'reconnecting') return 'connecting'
  return 'off'
})
const label = computed(() => ({ live: 'Live', polling: 'Polling', connecting: 'Connecting', off: 'Offline' })[state.value])
const ago = computed(() => (live.lastUpdatedAt ? fmtAgo(live.lastUpdatedAt, now.value) : 'never'))
const title = computed(() => {
  const base = { live: 'Connected to the event stream (SSE).', polling: 'Event stream unavailable — refreshing every 10 s.', connecting: 'Connecting to the event stream…', off: 'Not connected.' }[state.value]
  return live.detail ? `${base} ${live.detail}` : base
})
</script>

<template>
  <span class="live" :class="state" :title="title" role="status" aria-live="off">
    <span class="dot" aria-hidden="true"></span>
    <span class="txt">{{ label }}<span class="ago"> · updated <span class="num">{{ ago }}</span></span></span>
  </span>
</template>

<style scoped>
.live {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  font-size: var(--text-xs);
  color: var(--text-muted);
  white-space: nowrap;
  font-variant-numeric: tabular-nums;
}
.dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--text-faint);
  box-shadow: 0 0 0 0 transparent;
}
.live.live .dot {
  background: var(--success);
  animation: pulse 2.4s ease-out infinite;
}
.live.polling .dot {
  background: var(--warning);
}
.live.connecting .dot {
  background: var(--text-muted);
}
.txt {
  font-weight: 500;
  color: var(--text);
}
.ago {
  font-weight: 400;
  color: var(--text-muted);
}
@keyframes pulse {
  0% {
    box-shadow: 0 0 0 0 rgba(67, 122, 34, 0.35);
  }
  70% {
    box-shadow: 0 0 0 6px rgba(67, 122, 34, 0);
  }
  100% {
    box-shadow: 0 0 0 0 rgba(67, 122, 34, 0);
  }
}
@media (prefers-reduced-motion: reduce) {
  .live.live .dot {
    animation: none;
  }
}
@media (max-width: 480px) {
  .ago {
    display: none;
  }
}
</style>
