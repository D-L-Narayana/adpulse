<script setup lang="ts">
import Toast from './components/ui/Toast.vue'
</script>

<template>
  <a href="#main" class="skip">Skip to content</a>
  <router-view />
  <Toast />
</template>

<style>
.skip {
  position: absolute;
  left: 8px;
  top: -48px;
  z-index: 100;
  background: var(--accent);
  color: #fff;
  padding: 8px 12px;
  border-radius: var(--r-md);
  font-weight: 500;
  transition: top var(--dur-fast) var(--ease);
}
.skip:focus {
  top: 8px;
}
</style>
