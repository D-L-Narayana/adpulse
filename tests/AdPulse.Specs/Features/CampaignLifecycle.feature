Feature: Campaign lifecycle
  Campaigns move Draft -> Active -> Paused/Completed and every transition is validated.

  Scenario: Activate a draft campaign
    Given a tenant "acme" with API key "acme-key"
    And a draft campaign "Spring Launch"
    When I activate the campaign
    Then the campaign status is "Active"

  Scenario: Cannot activate a campaign whose end date has passed
    Given a tenant "acme" with API key "acme-key"
    And a draft campaign "Old Promo" that ended on 2024-01-31
    When I activate the campaign
    Then the response status is 422
    And the problem detail mentions "end date"
