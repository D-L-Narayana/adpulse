<script setup lang="ts">
import EmptyState from '../components/ui/EmptyState.vue'
import Button from '../components/ui/Button.vue'
import { lastSlug } from '../stores/session'
const last = lastSlug()
</script>

<template>
  <main id="main" class="nf" tabindex="-1">
    <EmptyState title="There is nothing at this address" description="Tenants live under /t/<slug>. Check the link, or go back to the start.">
      <Button variant="primary" to="/">Go to start</Button>
      <Button v-if="last" variant="ghost" :to="`/t/${last}`">Open {{ last }}</Button>
    </EmptyState>
  </main>
</template>

<style scoped>
.nf {
  max-width: 560px;
  margin: 10vh auto;
  padding: 0 var(--s-4);
  outline: none;
}
</style>
