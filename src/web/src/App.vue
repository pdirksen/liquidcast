<script setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useAuth } from './stores/auth'
import { LOCALES, setLocale } from './i18n'
import Toast from 'primevue/toast'
import ConfirmDialog from 'primevue/confirmdialog'
import Select from 'primevue/select'
import AccountMenu from './components/AccountMenu.vue'

const auth = useAuth()
const route = useRoute()
const { t, locale } = useI18n()

const showChrome = computed(() => auth.user && route.name !== 'login')

// Two groups: observe (monitor/stats), then the content pipeline. Settings lives
// on the right as a gear. "Tracks" and "Playlists" stay literal in every language.
const navGroups = computed(() => [
  [
    { to: '/monitor', label: t('nav.monitor'), icon: 'pi-chart-bar' },
    { to: '/stats', label: t('nav.stats'), icon: 'pi-chart-line' },
  ],
  [
    { to: '/tracks', label: 'Tracks', icon: 'pi-database' },
    { to: '/playlists', label: 'Playlists', icon: 'pi-list' },
    { to: '/schedule', label: t('nav.schedule'), icon: 'pi-calendar' },
  ],
])

const locales = LOCALES
const currentLocale = computed({
  get: () => locale.value,
  set: (v) => setLocale(v),
})

function flagSrc(code) {
  const c = LOCALES.find((l) => l.code === code)?.country
  return c ? `https://flagcdn.com/h20/${c}.png` : ''
}

</script>

<template>
  <div class="dark app-shell">
    <Toast />
    <ConfirmDialog />
    <header v-if="showChrome" class="topbar">
      <div class="brand"><i class="pi pi-wifi" /> Liquidcast</div>
      <nav class="nav">
        <template v-for="(group, gi) in navGroups" :key="gi">
          <span v-if="gi > 0" class="nav-divider" />
          <RouterLink v-for="n in group" :key="n.to" :to="n.to" class="navlink">
            <i :class="['pi', n.icon]" /> {{ n.label }}
          </RouterLink>
        </template>
      </nav>
      <div class="spacer" />
      <RouterLink to="/settings" class="gear" :title="t('nav.settings')">
        <i class="pi pi-cog" />
      </RouterLink>
      <AccountMenu />
      <Select v-model="currentLocale" :options="locales" optionValue="code" class="lang">
        <template #value="{ value }">
          <img v-if="value" :src="flagSrc(value)" :alt="value" class="flag" />
        </template>
        <template #option="{ option }">
          <img :src="flagSrc(option.code)" :alt="option.label" class="flag" />
          <span class="flag-label">{{ option.label }}</span>
        </template>
      </Select>
    </header>
    <main><RouterView /></main>
  </div>
</template>

<style scoped>
.app-shell { min-height: 100%; }
.topbar {
  display: flex; align-items: center; gap: 1rem;
  padding: .6rem 1.25rem; background: var(--surface); border-bottom: 1px solid var(--border);
  position: sticky; top: 0; z-index: 10;
}
.brand { font-weight: 700; letter-spacing: .3px; display: flex; align-items: center; gap: .5rem; }
.brand .pi { color: var(--accent); }
.nav { display: flex; gap: .25rem; }
.navlink {
  padding: .4rem .7rem; border-radius: 8px; color: var(--text-muted); font-size: .92rem;
  display: flex; align-items: center; gap: .4rem;
}
.navlink:hover { background: var(--surface-3); color: var(--text-strong); }
.navlink.router-link-active { background: var(--accent-soft); color: var(--accent-contrast); }
.nav-divider { width: 1px; align-self: stretch; margin: .35rem .5rem; background: var(--border); }
.gear { display: inline-flex; align-items: center; justify-content: center; color: var(--text-muted);
  border: 1px solid var(--border); border-radius: 8px; padding: .4rem .55rem; margin-right: .25rem; }
.gear:hover { color: var(--text-strong); border-color: var(--border-strong); }
.gear.router-link-active { color: var(--accent-contrast); background: var(--accent-soft); border-color: var(--accent); }
.lang { margin-right: .5rem; }
.lang :deep(.p-select-dropdown) { width: 1.6rem; }
.flag { width: 22px; height: 16px; border-radius: 2px; object-fit: cover; display: block; }
.flag-label { margin-left: .5rem; }
:deep(.p-select-option) { display: flex; align-items: center; }
</style>
