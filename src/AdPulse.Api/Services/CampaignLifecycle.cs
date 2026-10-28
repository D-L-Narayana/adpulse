using AdPulse.Api.Domain;

namespace AdPulse.Api.Services;

/// <summary>
/// Explicit state machine for the campaign lifecycle.
///
///   Draft ──activate──▶ Active ──pause──▶ Paused
///                        ▲                  │
///                        └────activate──────┘
///   Active | Paused ──complete──▶ Completed (terminal, read-only)
///
/// Kept as a pure table so both the endpoints and the tests reason about the same source of truth.
/// </summary>
public static class CampaignLifecycle
{
    public static readonly IReadOnlyDictionary<string, (CampaignStatus To, CampaignStatus[] From)> Actions =
        new Dictionary<string, (CampaignStatus, CampaignStatus[])>(StringComparer.OrdinalIgnoreCase)
        {
            ["activate"] = (CampaignStatus.Active, [CampaignStatus.Draft, CampaignStatus.Paused]),
            ["pause"] = (CampaignStatus.Paused, [CampaignStatus.Active]),
            ["complete"] = (CampaignStatus.Completed, [CampaignStatus.Active, CampaignStatus.Paused]),
        };

    public static bool CanTransition(CampaignStatus from, CampaignStatus to) =>
        Actions.Values.Any(a => a.To == to && a.From.Contains(from));

    /// <summary>Returns an error code + detail when the transition is not allowed; null when it is.</summary>
    public static (int Status, string Code, string Detail)? Validate(Campaign c, string action, DateOnly today)
    {
        if (!Actions.TryGetValue(action, out var a))
            return (404, "unknown_action", $"Unknown lifecycle action '{action}'.");
        if (c.Status == a.To)
            return (409, "invalid_transition", $"Campaign is already {a.To}.");
        if (!a.From.Contains(c.Status))
            return (409, "invalid_transition", $"Cannot move a {c.Status} campaign to {a.To}. Allowed from: {string.Join(", ", a.From)}.");
        if (a.To == CampaignStatus.Active && c.EndDate < today)
            return (422, "flight_ended", $"Cannot activate: the campaign end date {c.EndDate:yyyy-MM-dd} has already passed.");
        return null;
    }
}
