<script setup lang="ts">
import type { CrossChannelReport } from '../types'
import { channelLabel, fmtInt, fmtMoney, fmtMoney2, fmtPct, fmtX } from '../format'

defineProps<{ report: CrossChannelReport }>()
</script>

<template>
  <div class="table-wrap">
    <table>
      <thead>
        <tr>
          <th>Channel</th>
          <th class="num">Spend</th>
          <th class="num">Conv.</th>
          <th class="num">CTR</th>
          <th class="num">CPC</th>
          <th class="num">CPA</th>
          <th class="num">ROAS</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="r in report.channels" :key="r.channel">
          <td>
            <div class="cell-main">{{ channelLabel[r.channel] ?? r.channel }}</div>
            <div class="cell-sub">{{ r.campaigns }} campaign{{ r.campaigns === 1 ? '' : 's' }} · {{ fmtInt(r.impressions) }} impr.</div>
          </td>
          <td class="num">{{ fmtMoney(r.spend) }}</td>
          <td class="num">{{ fmtInt(r.conversions) }}</td>
          <td class="num">{{ fmtPct(r.ctr) }}</td>
          <td class="num">{{ fmtMoney2(r.cpc) }}</td>
          <td class="num">{{ fmtMoney2(r.cpa) }}</td>
          <td class="num">{{ fmtX(r.roas) }}</td>
        </tr>
        <tr v-if="!report.channels.length"><td colspan="7" class="empty">No spend in this period.</td></tr>
        <tr v-else class="total">
          <td>All channels</td>
          <td class="num">{{ fmtMoney(report.total.spend) }}</td>
          <td class="num">{{ fmtInt(report.total.conversions) }}</td>
          <td class="num">{{ fmtPct(report.total.ctr) }}</td>
          <td class="num">{{ fmtMoney2(report.total.cpc) }}</td>
          <td class="num">{{ fmtMoney2(report.total.cpa) }}</td>
          <td class="num">{{ fmtX(report.total.roas) }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
