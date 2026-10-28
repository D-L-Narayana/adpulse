import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'

const routes: RouteRecordRaw[] = [
  { path: '/', name: 'home', component: () => import('./pages/LandingPage.vue'), meta: { title: 'AdPulse' } },
  {
    path: '/t/:slug',
    component: () => import('./layouts/TenantLayout.vue'),
    props: true,
    children: [
      { path: '', name: 'dashboard', component: () => import('./pages/DashboardPage.vue'), meta: { title: 'Dashboard' } },
      { path: 'campaigns/:id', name: 'campaign', component: () => import('./pages/CampaignDetailPage.vue'), meta: { title: 'Campaign' } },
      { path: 'import', name: 'import', component: () => import('./pages/ImportPage.vue'), meta: { title: 'Import' } },
      { path: 'alerts', name: 'alerts', component: () => import('./pages/AlertsPage.vue'), meta: { title: 'Alerts' } },
      { path: 'settings', name: 'settings', component: () => import('./pages/SettingsPage.vue'), meta: { title: 'Settings' } },
    ],
  },
  { path: '/:pathMatch(.*)*', name: 'not-found', component: () => import('./pages/NotFoundPage.vue'), meta: { title: 'Not found' } },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior(to, _from, saved) {
    if (saved) return saved
    if (to.hash) return { el: to.hash }
    return { top: 0 }
  },
})

router.afterEach((to) => {
  const slug = typeof to.params.slug === 'string' ? to.params.slug : null
  const title = (to.meta.title as string | undefined) ?? 'AdPulse'
  document.title = slug && title !== 'AdPulse' ? `${title} · ${slug} · AdPulse` : title === 'AdPulse' ? 'AdPulse — campaign spend & budget pacing, live' : `${title} · AdPulse`
})
