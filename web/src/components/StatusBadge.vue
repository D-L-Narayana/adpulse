<script setup lang="ts">
import type { CampaignStatus } from '../types'

const props = defineProps<{ status: CampaignStatus }>()
const tone: Record<CampaignStatus, string> = { Draft: 'neutral', Active: 'ok', Paused: 'warn', Completed: 'info' }
const cls = () => tone[props.status] ?? 'neutral'
</script>

<template>
  <span class="badge" :class="cls()">{{ status }}</span>
</template>
