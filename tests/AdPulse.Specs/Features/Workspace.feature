Feature: Self-serve workspace and data import
  A visitor creates a workspace with a name and an email and receives an API key once.
  Real ad-platform exports are detected and previewed before anything is written.

  Scenario: Signing up returns the API key once and stores only its hash
    When a visitor signs up as "Acme Retail" with email "ops@acme.test"
    Then the response status is 201
    And the signup response contains an API key starting with "adp_live_"
    And the workspace slug starts with "acme-retail"
    And the stored tenant holds the SHA-256 of that key, not the key

  Scenario: A new workspace is empty and isolated
    Given a tenant "acme" with API key "acme-key"
    And an active campaign "Acme Only"
    When a visitor signs up as "Globex Travel" with email "ops@globex.test"
    And the new workspace lists its campaigns
    Then the list does not contain "Acme Only"

  Scenario: A Google Ads export is detected on dry run without writing anything
    When a visitor signs up as "Initech" with email "ops@initech.test"
    And the new workspace previews a Google Ads CSV export
    Then the detected format is "google-ads"
    And the column "spend" is mapped to "Cost"
    When the new workspace lists its campaigns
    Then the list is empty

  Scenario: A Meta Ads Manager export is committed and audited
    When a visitor signs up as "Umbrella" with email "ops@umbrella.test"
    And the new workspace imports a Meta Ads Manager CSV export
    Then the response status is 201
    And the detected format is "meta-ads"
    And the import history has 1 entry
