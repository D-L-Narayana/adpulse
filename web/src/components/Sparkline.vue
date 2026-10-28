<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{ values: number[]; width?: number; height?: number }>(), { width: 64, height: 22 })
const d = computed(() => {
  const v = props.values
  if (v.length < 2) return ''
  const min = Math.min(...v)
  const max = Math.max(...v)
  const span = max - min || 1
  const stepX = props.width / (v.length - 1)
  return v.map((n, i) => `${i === 0 ? 'M' : 'L'}${(i * stepX).toFixed(1)},${(props.height - 2 - ((n - min) / span) * (props.height - 4)).toFixed(1)}`).join(' ')
})
</script>

<template>
  <svg :width="width" :height="height" :viewBox="`0 0 ${width} ${height}`" aria-hidden="true" focusable="false">
    <path :d="d" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round" stroke-linecap="round" />
  </svg>
</template>
