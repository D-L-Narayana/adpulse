Feature: Server-side KPIs
  CTR, CPC, CPA and ROAS are computed on the server from daily spend rows
  so every client (dashboard, exports, alerts) sees identical numbers.

  Background:
    Given a tenant "acme" with API key "acme-key"
    And an active campaign "Search - Brand"

  Scenario: CTR and CPC are derived from impressions, clicks and spend
    Given the campaign recorded 20000 impressions, 500 clicks, 25 conversions, 250.00 spend and 1250.00 revenue
    When I request the campaign KPIs
    Then the CTR is 2.50
    And the CPC is 0.50

  Scenario: CPA and ROAS are derived from conversions, spend and revenue
    Given the campaign recorded 20000 impressions, 500 clicks, 25 conversions, 250.00 spend and 1250.00 revenue
    When I request the campaign KPIs
    Then the CPA is 10.00
    And the ROAS is 5.00

  Scenario: KPIs are zero-safe when there is no traffic yet
    When I request the campaign KPIs
    Then the CTR is 0.00
    And the CPC is 0.00
    And the CPA is 0.00
    And the ROAS is 0.00
