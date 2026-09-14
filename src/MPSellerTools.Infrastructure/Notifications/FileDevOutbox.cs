using System.Text.Json;
using MPSellerTools.Core.Notifications;

namespace MPSellerTools.Infrastructure.Notifications;

/// <summary>
/// Writes each notification as a JSON file under a per-instance outbox
/// directory (never inside wwwroot, never tracked by Git — see the host's
/// startup configuration for the actual path).
/// </summary>
public class FileDevOutbox(string outboxDirectory) : IDevOutbox
{
    public async Task WriteAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outboxDirectory);

        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json";
        var path = Path.Combine(outboxDirectory, fileName);

        var payload = new
        {
            To = to,
            Subject = subject,
            Body = body,
            SentAtUtc = DateTime.UtcNow,
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
