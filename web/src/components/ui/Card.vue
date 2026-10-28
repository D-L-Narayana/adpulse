<script setup lang="ts">
withDefaults(defineProps<{ title?: string; subtitle?: string; padded?: boolean; as?: string }>(), { padded: true, as: 'section' })
</script>

<template>
  <component :is="as" class="card" :class="{ padded }">
    <header v-if="title || $slots.title || $slots.actions" class="card-head">
      <div class="titles">
        <slot name="title">
          <h2 class="card-title">{{ title }}</h2>
        </slot>
        <p v-if="subtitle" class="card-sub">{{ subtitle }}</p>
      </div>
      <div v-if="$slots.actions" class="actions"><slot name="actions" /></div>
    </header>
    <slot />
  </component>
</template>

<style scoped>
.card {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--r-lg);
  box-shadow: var(--shadow-sm);
  min-width: 0;
}
.card.padded {
  padding: var(--s-5);
}
.card-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--s-3);
  margin-bottom: var(--s-4);
}
.card:not(.padded) .card-head {
  padding: var(--s-5) var(--s-5) 0;
}
.titles {
  min-width: 0;
}
.card-title {
  font-size: var(--text-md);
  font-weight: 700;
  letter-spacing: 0;
}
.card-sub {
  font-size: var(--text-sm);
  color: var(--text-muted);
  margin-top: 2px;
}
.actions {
  display: flex;
  align-items: center;
  gap: var(--s-2);
  flex-shrink: 0;
}
@media (max-width: 640px) {
  .card.padded {
    padding: var(--s-4);
  }
  .card:not(.padded) .card-head {
    padding: var(--s-4) var(--s-4) 0;
  }
}
</style>
