using AdPulse.Api.Services;

namespace AdPulse.Api.Endpoints;

public static class PacingEndpoints
{
    public static IEndpointRouteBuilder MapPacing(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/pacing").WithTags("Pacing");

        // Same evaluation the daily job runs, on demand for the calling tenant. Emits pacing.alert on the stream.
        g.MapPost("/evaluate", async (PacingEvaluator evaluator, DateOnly? asOf) =>
            Results.Ok(await evaluator.EvaluateAsync(asOf)))
         .WithSummary($"Evaluate active campaigns now; returns + streams pacing.alert for those > {PacingEvaluator.AlertBandPercent}% off pace");

        g.MapGet("/alerts", async (PacingEvaluator evaluator, DateOnly? asOf) =>
            Results.Ok(await evaluator.EvaluateAsync(asOf, publish: false)))
         .WithSummary("Current pacing alerts without emitting events");

        return app;
    }
}
