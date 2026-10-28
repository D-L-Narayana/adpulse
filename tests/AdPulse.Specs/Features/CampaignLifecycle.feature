Feature: Campaign lifecycle
  Campaigns move Draft -> Active <-> Paused -> Completed and every transition is validated.
  Invalid transitions are rejected with an RFC 7807 problem+json body.

  Background:
    Given a tenant "acme" with API key "acme-key"

  Scenario: Activate a draft campaign
    Given a draft campaign "Spring Launch"
    When I activate the campaign
    Then the response status is 200
    And the campaign status is "Active"

  Scenario: Pause an active campaign
    Given an active campaign "Spring Launch"
    When I pause the campaign
    Then the campaign status is "Paused"

  Scenario: Resume a paused campaign
    Given a paused campaign "Spring Launch"
    When I activate the campaign
    Then the campaign status is "Active"

  Scenario: Complete an active campaign
    Given an active campaign "Spring Launch"
    When I complete the campaign
    Then the campaign status is "Completed"
    And editing the campaign is rejected with 409 and problem type "campaign_completed"

  Scenario: Cannot pause a draft campaign
    Given a draft campaign "Spring Launch"
    When I pause the campaign
    Then the response status is 409
    And the response is an RFC 7807 problem with type "invalid_transition"
    And the problem detail mentions "Draft"
    And the campaign status is "Draft"

  Scenario: Cannot activate a campaign whose end date has passed
    Given a draft campaign "Old Promo" that ended on 2024-01-31
    When I activate the campaign
    Then the response status is 422
    And the response is an RFC 7807 problem with type "flight_ended"
    And the problem detail mentions "end date"
