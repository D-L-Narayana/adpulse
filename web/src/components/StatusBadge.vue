<script setup lang="ts">
import { computed } from 'vue'
import Badge from './ui/Badge.vue'

const props = defineProps<{ status: string }>()
const tone = computed(() => (({ Active: 'ok', Paused: 'warn', Completed: 'neutral', Draft: 'info' }) as Record<string, 'ok' | 'warn' | 'neutral' | 'info'>)[props.status] ?? 'neutral')
</script>

<template>
  <Badge :tone="tone">{{ status }}</Badge>
</template>
