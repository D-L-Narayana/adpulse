Feature: Budget pacing
  As an advertiser
  I want to know whether a campaign is under- or over-spending its budget
  So that I can correct the daily budget before the flight ends

  Background:
    Given a tenant "acme" with API key "acme-key"
    And a campaign "Diwali Sale" running from 2025-10-01 to 2025-10-10 with a daily budget of 1000.00

  Scenario: Campaign is on track
    Given the campaign has spent 5000.00 by 2025-10-05
    When I request pacing as of 2025-10-05
    Then the pacing percentage is 100.0
    And the pacing status is "on-track"
    And the suggested daily budget is 1000.00

  Scenario: Campaign is under-pacing beyond the 10% band
    Given the campaign has spent 3000.00 by 2025-10-05
    When I request pacing as of 2025-10-05
    Then the pacing percentage is 60.0
    And the pacing status is "under"
    And the suggested daily budget is 1400.00

  Scenario: Campaign is over-pacing beyond the 10% band
    Given the campaign has spent 6500.00 by 2025-10-05
    When I request pacing as of 2025-10-05
    Then the pacing percentage is 130.0
    And the pacing status is "over"
    And the suggested daily budget is 700.00

  Scenario: Spend within the 10% band is still on track
    Given the campaign has spent 5450.00 by 2025-10-05
    When I request pacing as of 2025-10-05
    Then the pacing status is "on-track"

  Scenario: Tenant isolation
    Given another tenant "globex" with API key "globex-key"
    When tenant "globex" requests pacing for the "Diwali Sale" campaign
    Then the response status is 404
