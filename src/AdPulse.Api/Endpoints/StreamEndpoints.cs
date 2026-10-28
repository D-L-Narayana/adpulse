using AdPulse.Api.Services;
using AdPulse.Api.Tenancy;

namespace AdPulse.Api.Endpoints;

public static class StreamEndpoints
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    public static IEndpointRouteBuilder MapStream(this IEndpointRouteBuilder app)
    {
        // Server-Sent Events. Auth: X-Api-Key header, or ?api_key= for browsers' EventSource (which cannot set headers).
        // Event names: metrics.upserted | campaign.changed | import.completed | pacing.alert | heartbeat (every 15 s).
        app.MapGet("/api/v1/stream", async (HttpContext http, EventBus bus, TenantContext tenant, IHostApplicationLifetime life) =>
        {
            var res = http.Response;
            res.StatusCode = 200;
            res.Headers.ContentType = "text/event-stream";
            res.Headers.CacheControl = "no-cache";
            res.Headers.Connection = "keep-alive";
            res.Headers["X-Accel-Buffering"] = "no"; // tell proxies not to buffer
            http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, life.ApplicationStopping);
            var ct = cts.Token;
            using var sub = bus.Subscribe(tenant.TenantId);
            long id = 0;

            await res.WriteAsync($": connected tenant={tenant.Tenant!.Slug}\nretry: 3000\n\n", ct);
            await res.WriteAsync(Sse.Format(ServerEvent.Of(EventTypes.Heartbeat, new { tenant = tenant.Tenant.Slug, subscribers = bus.SubscriberCount(tenant.TenantId) }), ++id), ct);
            await res.Body.FlushAsync(ct);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var readTask = sub.Reader.WaitToReadAsync(ct).AsTask();
                    var completed = await Task.WhenAny(readTask, Task.Delay(HeartbeatInterval, ct));
                    if (completed == readTask)
                    {
                        if (!await readTask) break; // channel completed
                        while (sub.Reader.TryRead(out var evt))
                            await res.WriteAsync(Sse.Format(evt, ++id), ct);
                    }
                    else
                    {
                        await res.WriteAsync(Sse.Format(ServerEvent.Of(EventTypes.Heartbeat, new { utc = DateTime.UtcNow }), ++id), ct);
                    }
                    await res.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { /* client went away or app is stopping */ }
            catch (IOException) { /* connection reset */ }
        })
        .WithTags("Live")
        .WithSummary("Server-Sent Events: metrics.upserted, campaign.changed, import.completed, pacing.alert (+ heartbeat every 15 s)")
        .Produces(200, contentType: "text/event-stream");

        return app;
    }
}
