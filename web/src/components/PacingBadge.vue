<script setup lang="ts">
import { computed } from 'vue'
import { fmtMoney } from '../lib/format'
import { pacingView } from '../lib/pacing'
import type { PacingStatus } from '../lib/types'
import Badge from './ui/Badge.vue'

const props = withDefaults(
  defineProps<{
    status?: PacingStatus
    percent: number
    expected?: number
    actual?: number
    campaignStatus?: string
    /** show the expected vs actual line under the badge */
    detail?: boolean
  }>(),
  { detail: false },
)
const view = computed(() => pacingView({ pacingPercent: props.percent, status: props.status, expectedSpendToDate: props.expected, actualSpendToDate: props.actual, campaignStatus: props.campaignStatus }, fmtMoney))
</script>

<template>
  <div class="pb" :class="{ detail }">
    <Badge :tone="view.tone" :title="view.description">
      <span>{{ view.label }}</span>
      <span v-if="view.deltaLabel" class="num pct">{{ view.deltaLabel }}</span>
    </Badge>
    <span class="sr-only">{{ view.description }}</span>
    <p v-if="detail && view.status !== 'not-started' && expected !== undefined && actual !== undefined" class="ea num" aria-hidden="true">
      <span>{{ fmtMoney(actual) }}</span><span class="sep"> of </span><span>{{ fmtMoney(expected) }}</span><span class="sep"> expected</span>
    </p>
  </div>
</template>

<style scoped>
.pb {
  display: inline-flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 3px;
}
.pct {
  font-weight: 700;
}
.ea {
  font-size: var(--text-xs);
  color: var(--text-muted);
  white-space: nowrap;
}
</style>
