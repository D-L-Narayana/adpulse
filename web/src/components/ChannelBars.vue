<script setup lang="ts">
/** Ranked horizontal bars with direct labels — no legend, no chart library needed. */
import { computed } from 'vue'
import { channelLabel, fmtInt, fmtMoney, fmtMoney2, fmtPct } from '../lib/format'
import type { KpiRow } from '../lib/types'
import Skeleton from './ui/Skeleton.vue'

const props = withDefaults(defineProps<{ rows: KpiRow[]; total?: KpiRow | null; loading?: boolean }>(), { loading: false, total: null })
const palette = ['var(--chart-1)', 'var(--chart-2)', 'var(--chart-3)', 'var(--chart-5)', 'var(--chart-4)']
const sorted = computed(() => [...props.rows].sort((a, b) => b.spend - a.spend))
const max = computed(() => Math.max(1, ...sorted.value.map((r) => r.spend)))
const totalSpend = computed(() => props.total?.spend ?? sorted.value.reduce((a, r) => a + r.spend, 0))
const insight = computed(() => {
  if (!sorted.value.length) return 'No channel spend in this period'
  const top = sorted.value[0]
  const share = totalSpend.value ? (top.spend / totalSpend.value) * 100 : 0
  return `${channelLabel(top.channel)} is ${share.toFixed(0)}% of spend across ${sorted.value.length} channel${sorted.value.length === 1 ? '' : 's'}`
})
</script>

<template>
  <div class="cb">
    <p class="insight"><Skeleton v-if="loading" width="55%" height="16px" :inline="true" /><template v-else>{{ insight }}</template></p>
    <ul v-if="loading" class="list" aria-busy="true">
      <li v-for="i in 3" :key="i" class="item">
        <Skeleton width="30%" height="14px" /><Skeleton :width="`${90 - i * 20}%`" height="10px" radius="5px" style="margin-top: 6px" />
      </li>
    </ul>
    <p v-else-if="!sorted.length" class="none">Import or push metrics to see the channel split.</p>
    <ul v-else class="list" :aria-label="`Spend by channel. ${insight}`">
      <li v-for="(r, i) in sorted" :key="r.channel" class="item">
        <div class="line">
          <span class="name">{{ channelLabel(r.channel) }} <span class="muted">· {{ r.campaigns }} camp.</span></span>
          <span class="num val">{{ fmtMoney(r.spend) }} <span class="muted share">{{ totalSpend ? ((r.spend / totalSpend) * 100).toFixed(0) : 0 }}%</span></span>
        </div>
        <div class="track" role="img" :aria-label="`${channelLabel(r.channel)}: ${fmtMoney(r.spend)}`">
          <div class="bar" :style="{ width: `${(r.spend / max) * 100}%`, background: palette[i % palette.length] }"></div>
        </div>
        <div class="sub num">
          <span>{{ fmtInt(r.clicks) }} clicks</span><span>CTR {{ fmtPct(r.ctr) }}</span><span>CPC {{ fmtMoney2(r.cpc) }}</span><span>{{ fmtInt(r.conversions) }} conv.</span>
        </div>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.insight {
  font-size: var(--text-sm);
  font-weight: 500;
  margin-bottom: var(--s-4);
}
.list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: var(--s-4);
}
.line {
  display: flex;
  justify-content: space-between;
  gap: var(--s-3);
  font-size: var(--text-sm);
  margin-bottom: 6px;
}
.name {
  font-weight: 500;
}
.val {
  font-weight: 700;
}
.share {
  font-weight: 400;
}
.track {
  height: 10px;
  background: var(--surface-sunken);
  border-radius: 5px;
  overflow: hidden;
}
.bar {
  height: 100%;
  border-radius: 5px;
  transition: width var(--dur-slow) var(--ease);
}
.sub {
  display: flex;
  gap: var(--s-3);
  flex-wrap: wrap;
  margin-top: 6px;
  font-size: var(--text-xs);
  color: var(--text-muted);
}
.none {
  color: var(--text-muted);
  font-size: var(--text-sm);
  padding: var(--s-6) 0;
  text-align: center;
  border: 1px dashed var(--border);
  border-radius: var(--r-md);
}
</style>
