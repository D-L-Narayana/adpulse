<script setup lang="ts">
import { computed } from 'vue'
import type { PacingStatus } from '../types'
import { fmtMoney2 } from '../format'

const props = defineProps<{ status: PacingStatus; percent: number; dailyBudget: number; suggested: number }>()

const meta: Record<PacingStatus, { label: string; tone: string; color: string }> = {
  'on-track': { label: 'On track', tone: 'ok', color: '#16a34a' },
  under: { label: 'Under-spending', tone: 'warn', color: '#d97706' },
  over: { label: 'Over-spending', tone: 'bad', color: '#dc2626' },
  'not-started': { label: 'Not started', tone: 'neutral', color: '#94a3b8' },
}
const m = computed(() => meta[props.status] ?? meta['not-started'])
// bar scale: 0..200 % maps to 0..100 % width, 100 % sits at the centre marker
const width = computed(() => Math.max(0, Math.min(100, props.percent / 2)))
const delta = computed(() => props.suggested - props.dailyBudget)
</script>

<template>
  <div class="pace" :title="`${percent.toFixed(1)}% of expected spend to date (healthy band 90–110%)`">
    <span class="badge" :class="m.tone">{{ m.label }}<template v-if="status !== 'not-started'"> · {{ percent.toFixed(0) }}%</template></span>
    <div class="pace-bar">
      <div class="band"></div>
      <div class="fill" :style="{ width: width + '%', background: m.color }"></div>
      <div class="marker"></div>
    </div>
    <div class="pace-meta">
      <span>Budget {{ fmtMoney2(dailyBudget) }}/d</span>
      <span v-if="status === 'under' || status === 'over'" :style="{ color: m.color, fontWeight: 600 }">
        → {{ fmtMoney2(suggested) }}/d ({{ delta >= 0 ? '+' : '' }}{{ fmtMoney2(delta) }})
      </span>
      <span v-else-if="status === 'on-track'">keep {{ fmtMoney2(suggested) }}/d</span>
    </div>
  </div>
</template>
