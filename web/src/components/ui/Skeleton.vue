<script setup lang="ts">
withDefaults(defineProps<{ width?: string; height?: string; radius?: string; inline?: boolean }>(), { width: '100%', height: '1em', radius: '4px', inline: false })
</script>

<template>
  <span class="sk" :class="{ inline }" :style="{ width, height, borderRadius: radius }" aria-hidden="true"></span>
</template>

<style scoped>
.sk {
  display: block;
  background: linear-gradient(90deg, var(--surface-sunken) 0%, #ebe9e3 50%, var(--surface-sunken) 100%);
  background-size: 200% 100%;
  animation: shimmer 1.4s ease-in-out infinite;
}
.sk.inline {
  display: inline-block;
  vertical-align: middle;
}
@keyframes shimmer {
  from {
    background-position: 200% 0;
  }
  to {
    background-position: -200% 0;
  }
}
@media (prefers-reduced-motion: reduce) {
  .sk {
    animation: none;
    background: var(--surface-sunken);
  }
}
</style>
