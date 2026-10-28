<script setup lang="ts">
import { computed, ref } from 'vue'
import type { CampaignSummary, CampaignStatus } from '../types'
import { channelLabel, fmtDate, fmtInt, fmtMoney, fmtMoney2, fmtPct, fmtX } from '../format'
import StatusBadge from './StatusBadge.vue'
import PacingBadge from './PacingBadge.vue'

const props = defineProps<{ rows: CampaignSummary[]; live: boolean; busy: string | null }>()
const emit = defineEmits<{ (e: 'action', id: string, action: 'activate' | 'pause' | 'complete'): void }>()

const filter = ref<CampaignStatus | 'All'>('All')
const statuses: (CampaignStatus | 'All')[] = ['All', 'Active', 'Paused', 'Draft', 'Completed']
const visible = computed(() => (filter.value === 'All' ? props.rows : props.rows.filter((r) => r.status === filter.value)))

const actionsFor = (s: CampaignStatus): ('activate' | 'pause' | 'complete')[] =>
  s === 'Draft' ? ['activate'] : s === 'Active' ? ['pause', 'complete'] : s === 'Paused' ? ['activate', 'complete'] : []
</script>

<template>
  <div>
    <div style="display: flex; gap: 6px; padding: 0 20px 12px; flex-wrap: wrap">
      <button v-for="s in statuses" :key="s" class="btn small" :class="{ primary: filter === s }" @click="filter = s">
        {{ s }} <span v-if="s !== 'All'" style="opacity: 0.7">({{ rows.filter((r) => r.status === s).length }})</span>
      </button>
    </div>
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Campaign</th>
            <th>Status</th>
            <th class="num">Spend</th>
            <th class="num">CTR</th>
            <th class="num">CPC</th>
            <th class="num">CPA</th>
            <th class="num">ROAS</th>
            <th>Budget pacing</th>
            <th v-if="live"></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="c in visible" :key="c.id">
            <td>
              <div class="cell-main">{{ c.name }}</div>
              <div class="cell-sub">{{ channelLabel[c.channel] ?? c.channel }} · {{ fmtDate(c.startDate) }} – {{ fmtDate(c.endDate) }} · {{ fmtInt(c.conversions) }} conv.</div>
            </td>
            <td><StatusBadge :status="c.status" /></td>
            <td class="num">{{ fmtMoney(c.spend) }}</td>
            <td class="num">{{ fmtPct(c.ctr) }}</td>
            <td class="num">{{ fmtMoney2(c.cpc) }}</td>
            <td class="num">{{ fmtMoney2(c.cpa) }}</td>
            <td class="num">{{ fmtX(c.roas) }}</td>
            <td><PacingBadge :status="c.pacingStatus" :percent="c.pacingPercent" :daily-budget="c.dailyBudget" :suggested="c.suggestedDailyBudget" /></td>
            <td v-if="live">
              <div class="row-actions">
                <button v-for="a in actionsFor(c.status)" :key="a" class="btn small" :disabled="busy === c.id" @click="emit('action', c.id, a)">{{ a }}</button>
              </div>
            </td>
          </tr>
          <tr v-if="!visible.length"><td :colspan="live ? 9 : 8" class="empty">No campaigns match this filter.</td></tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
