using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace AdPulse.Api.Services;

/// <summary>One server-sent event. <c>Type</c> is the SSE <c>event:</c> name, <c>Data</c> is serialised as JSON.</summary>
public sealed record ServerEvent(string Type, object Data, DateTime AtUtc)
{
    public static ServerEvent Of(string type, object data) => new(type, data, DateTime.UtcNow);
}

public static class EventTypes
{
    public const string MetricsUpserted = "metrics.upserted";
    public const string CampaignChanged = "campaign.changed";
    public const string ImportCompleted = "import.completed";
    public const string PacingAlert = "pacing.alert";
    public const string Heartbeat = "heartbeat";
}

/// <summary>
/// In-process pub/sub, one bounded <see cref="Channel{T}"/> per subscriber, subscribers grouped by tenant.
/// Publishing never blocks a request: a slow consumer drops its oldest events instead of back-pressuring writers.
/// Single-node by design (the API runs as one process); swap for Redis pub/sub if it ever scales out.
/// </summary>
public sealed class EventBus
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<ServerEvent>>> _tenants = new();
    private long _published;

    public long Published => Interlocked.Read(ref _published);
    public int SubscriberCount(Guid tenantId) => _tenants.TryGetValue(tenantId, out var subs) ? subs.Count : 0;

    public Subscription Subscribe(Guid tenantId, int capacity = 256)
    {
        var channel = Channel.CreateBounded<ServerEvent>(new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        var id = Guid.NewGuid();
        _tenants.GetOrAdd(tenantId, _ => new())[id] = channel;
        return new Subscription(channel.Reader, () =>
        {
            if (_tenants.TryGetValue(tenantId, out var subs))
            {
                subs.TryRemove(id, out _);
                if (subs.IsEmpty) _tenants.TryRemove(tenantId, out _);
            }
            channel.Writer.TryComplete();
        });
    }

    public void Publish(Guid tenantId, string type, object data)
    {
        Interlocked.Increment(ref _published);
        if (!_tenants.TryGetValue(tenantId, out var subs)) return;
        var evt = ServerEvent.Of(type, data);
        foreach (var ch in subs.Values) ch.Writer.TryWrite(evt);
    }

    public sealed class Subscription(ChannelReader<ServerEvent> reader, Action dispose) : IDisposable
    {
        public ChannelReader<ServerEvent> Reader { get; } = reader;
        public void Dispose() => dispose();
    }
}

/// <summary>Writes events in the <c>text/event-stream</c> wire format.</summary>
public static class Sse
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Format(ServerEvent e, long id)
    {
        var payload = JsonSerializer.Serialize(new { e.Type, e.AtUtc, e.Data }, Json);
        return $"id: {id}\nevent: {e.Type}\ndata: {payload}\n\n";
    }
}
