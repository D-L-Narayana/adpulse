<script setup lang="ts">
import { computed } from 'vue'
import { fmtSignedPct } from '../lib/format'
import Skeleton from './ui/Skeleton.vue'
import Sparkline from './Sparkline.vue'

const props = withDefaults(
  defineProps<{
    label: string
    value: string
    /** relative change vs previous period in %, null = n/a */
    delta?: number | null
    /** for cost metrics (CPC, CPA) a decrease is good */
    lowerIsBetter?: boolean
    hint?: string
    sub?: string
    spark?: number[]
    loading?: boolean
    periodLabel?: string
  }>(),
  { delta: null, lowerIsBetter: false, loading: false, periodLabel: 'vs previous period' },
)

const tone = computed(() => {
  if (props.delta === null || props.delta === undefined || Math.abs(props.delta) < 0.05) return 'flat'
  const up = props.delta > 0
  const good = props.lowerIsBetter ? !up : up
  return good ? 'good' : 'bad'
})
const showSpark = computed(() => !!props.spark && props.spark.length > 1 && props.spark.some((v) => v !== 0))
const arrow = computed(() => (props.delta === null || props.delta === undefined ? '' : props.delta > 0.05 ? '↑' : props.delta < -0.05 ? '↓' : '→'))
</script>

<template>
  <article class="kpi" :aria-busy="loading || undefined">
    <p class="label">{{ label }}<span v-if="hint" class="hint" :title="hint" aria-hidden="true"> ?</span><span v-if="hint" class="sr-only">. {{ hint }}</span></p>
    <template v-if="loading">
      <Skeleton width="70%" height="30px" radius="6px" />
      <Skeleton width="45%" height="14px" style="margin-top: 8px" />
    </template>
    <template v-else>
      <div class="value-row">
        <p class="value num">{{ value }}</p>
        <Sparkline v-if="showSpark" :values="spark!" class="spark" />
      </div>
      <p class="delta" :class="tone">
        <template v-if="delta !== null && delta !== undefined">
          <span class="arrow" aria-hidden="true">{{ arrow }}</span>
          <span class="num">{{ fmtSignedPct(delta, 1) }}</span>
          <span class="sr-only">{{ tone === 'good' ? 'improved' : tone === 'bad' ? 'worsened' : 'unchanged' }}</span>
          <span class="period long"> {{ periodLabel }}</span><span class="period short"> vs prev.</span>
        </template>
        <template v-else-if="sub"><span class="period">{{ sub }}</span></template>
        <template v-else><span class="period">no previous period</span></template>
      </p>
    </template>
  </article>
</template>

<style scoped>
.kpi {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--r-lg);
  padding: var(--s-4) var(--s-5);
  min-width: 0;
  box-shadow: var(--shadow-sm);
}
.label {
  font-size: var(--text-xs);
  font-weight: 500;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: var(--s-2);
}
.hint {
  display: inline-grid;
  place-items: center;
  width: 14px;
  height: 14px;
  border-radius: 50%;
  border: 1px solid var(--border);
  font-size: 10px;
  margin-left: 4px;
  text-transform: none;
  letter-spacing: 0;
  cursor: help;
  vertical-align: 1px;
}
.value-row {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: var(--s-2);
}
.value {
  font-size: 28px;
  font-weight: 700;
  line-height: 1.1;
  letter-spacing: -0.02em;
  font-variant-numeric: tabular-nums lining-nums;
  overflow: hidden;
  text-overflow: ellipsis;
}
.spark {
  flex-shrink: 0;
  color: var(--chart-1);
}
.delta {
  margin-top: 6px;
  font-size: var(--text-xs);
  color: var(--text-muted);
  display: flex;
  gap: 4px;
  align-items: baseline;
  flex-wrap: wrap;
  line-height: 1.35;
}
.delta .num {
  font-weight: 700;
}
.delta.good .arrow,
.delta.good .num {
  color: var(--success);
}
.delta.bad .arrow,
.delta.bad .num {
  color: var(--error);
}
.period {
  color: var(--text-muted);
}
.period.short {
  display: none;
}
@media (max-width: 640px) {
  .kpi {
    padding: var(--s-3) var(--s-4);
  }
  .value {
    font-size: 22px;
  }
  .spark {
    display: none;
  }
  .period.long {
    display: none;
  }
  .period.short {
    display: inline;
  }
}
</style>
