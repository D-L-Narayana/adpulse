Feature: Tenant isolation
  Every request is scoped to the tenant identified by its API key.
  Data from one advertiser must never be visible to another.

  Background:
    Given a tenant "acme" with API key "acme-key"
    And another tenant "globex" with API key "globex-key"
    And an active campaign "Acme Only"

  Scenario: Campaign list is tenant scoped
    When tenant "globex" lists campaigns
    Then the list does not contain "Acme Only"
    When tenant "acme" lists campaigns
    Then the list contains "Acme Only"

  Scenario: Direct lookup of a foreign campaign is 404
    When tenant "globex" requests the "Acme Only" campaign
    Then the response status is 404

  Scenario: Reports never include foreign spend
    Given the campaign has spent 900.00 by today
    When tenant "globex" requests the cross-channel report
    Then the report total spend is 0.00
    When tenant "acme" requests the cross-channel report
    Then the report total spend is 900.00

  Scenario: Requests without an API key are rejected
    When an anonymous client lists campaigns
    Then the response status is 401
    And the response is an RFC 7807 problem with type "missing_api_key"
