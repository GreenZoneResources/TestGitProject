namespace BulkReversal.Application.Common.Interfaces;

/// <summary>
/// Best-effort outbound email notification. Implementations must never let a delivery failure
/// (SMTP unreachable, bad credentials, etc.) propagate to the caller — an approval or submission
/// must still succeed even if the notification email cannot be sent; implementations log and
/// swallow such failures instead.
/// </summary>
public interface IEmailService
{
    Task SendAsync(IReadOnlyCollection<string> toAddresses, string subject, string htmlBody, CancellationToken ct = default);
}
