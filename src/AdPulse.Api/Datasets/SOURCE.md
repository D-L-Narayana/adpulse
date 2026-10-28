# Sample dataset — Facebook Ad Campaign (Kaggle, Madis Lemsalu)

| | |
|---|---|
| **Source** | https://www.kaggle.com/datasets/madislemsalu/facebook-ad-campaign |
| **Author / publisher** | Madis Lemsalu — "Courtesy of Bunq" (real campaign data from the Dutch fintech, anonymised) |
| **License** | **ODC Public Domain Dedication and Licence (PDDL) v1.0** — https://opendatacommons.org/licenses/pddl/1-0/ (verified on the Kaggle data card, 2026-10-28) |
| **Original file** | `data.csv`, 1 143 rows, 90 KB, 3 campaigns (`916`, `936`, `1178`), daily rows per ad for 17–30 Aug 2017 |
| **Vendored copy** | `kaggle_facebook_ad_campaign_raw.csv` — byte-identical apart from CRLF→LF |

PDDL places the data in the public domain: redistribution and transformation are permitted without attribution.
We attribute anyway because that is the honest thing to do.

## Why this one

It is one of the very few *real* (not simulated) paid-social datasets with a permissive license that has the
columns an ad-ops tool needs per day: impressions, clicks, spend and conversions. Everything else on Kaggle that
looks like "marketing campaign performance" is synthetic or carries an unknown license.

## Derived file served by `GET /api/v1/datasets/sample`

`facebook_ad_campaign_daily.csv` is produced from the raw file by:

1. Reading each ad-day row. 382 rows of campaign `1178` in the raw file are missing the `campaign_id` /
   `fb_campaign_id` cells (a known quirk of the upload — the remaining columns are shifted left by two). They are
   assigned to campaign `1178`, which is where their `ad_id` range (1121594–1314415) belongs.
2. Summing `impressions`, `clicks`, `spent` and `approved_conversion` per **campaign × reporting_start** (the
   dataset is already at day granularity: `reporting_start == reporting_end` on every row).
3. Writing the generic AdPulse import template: `campaign, date, channel, impressions, clicks, conversions, spend, revenue`.
   * `campaign` = `FB campaign <id>`; `channel` = `Meta`.
   * `conversions` = `approved_conversion` (purchases). `total_conversion` (enquiries) is not used.
   * `revenue` is **empty** — the dataset has no order value, so ROAS is shown as 0. We do not invent revenue.

Result: 35 campaign-days, 213 434 828 impressions, 38 165 clicks, 1 079 conversions, 58 705.23 spend after rounding each
campaign-day to cents (the raw rows sum to 58 705.21; currency is not stated by the source).

`POST /api/v1/datasets/sample/import?rebaseDates=true` shifts the 14-day window so that it ends today. That is an
option, off by default, and the import audit row records `sample-dataset` as the source so the data is never mistaken
for the user's own.
