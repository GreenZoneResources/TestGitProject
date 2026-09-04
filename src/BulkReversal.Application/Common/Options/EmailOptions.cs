namespace BulkReversal.Application.Common.Options;

/// <summary>
/// SMTP configuration for approval-routing notification emails (bound from appsettings.json
/// "Email"). Disabled by default so local/dev environments without an SMTP relay keep working —
/// the workflow itself never depends on email delivery succeeding.
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public bool Enabled { get; set; }

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 25;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = "bulkreversal-noreply@bank.local";
    public string FromName { get; set; } = "BulkReversal Settlement Portal";

    /// <summary>Base URL of the Settlement Portal frontend, used to build a direct link in
    /// notification emails (e.g. "https://intranet.bank.local/bulkreversal"). Optional.</summary>
    public string? PortalBaseUrl { get; set; }
}
