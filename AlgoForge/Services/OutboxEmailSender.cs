using System.Net;

namespace AlgoForge.Services
{
    // Dev/demo sender: writes each email to App_Data/outbox as an HTML file and keeps a
    // newest-first list in memory. The Outbox page renders them, so the invite, claim and
    // connection links are all clickable during a demo without an SMTP account.
    //
    // Replaces NoOpEmailSender, which only logged -- a link buried in console output is not
    // something you can hand to someone mid-presentation. Swap for a real provider before
    // anything but a local/demo run.
    public class OutboxEmailSender : IEmailSender
    {
        private readonly string _outboxRoot;
        private readonly ILogger<OutboxEmailSender> _logger;

        public OutboxEmailSender(IWebHostEnvironment environment, ILogger<OutboxEmailSender> logger)
        {
            _outboxRoot = Path.Combine(environment.ContentRootPath, "App_Data", "outbox");
            _logger = logger;
        }

        public Task SendEmailAsync(string toEmail, string subject, string body)
        {
            Directory.CreateDirectory(_outboxRoot);

            var safeSubject = string.Concat(subject.Split(Path.GetInvalidFileNameChars()));
            var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}__{Sanitise(toEmail)}__{Truncate(safeSubject, 60)}.html";

            File.WriteAllText(Path.Combine(_outboxRoot, fileName), Render(toEmail, subject, body));
            _logger.LogInformation("Outbox email written. To: {Email}, Subject: {Subject}", toEmail, subject);

            return Task.CompletedTask;
        }

        // The body is plain text written by the callers, so it is encoded rather than
        // trusted -- then bare URLs are turned into links, which is the whole point of
        // having a readable outbox.
        private static string Render(string toEmail, string subject, string body)
        {
            var encodedBody = WebUtility.HtmlEncode(body).Replace("\n", "<br />");
            var linked = System.Text.RegularExpressions.Regex.Replace(
                encodedBody,
                @"https?://[^\s<]+",
                m => $"<a href=\"{m.Value}\" style=\"color:#0ea5e9;font-weight:600;\">{m.Value}</a>");

            return $"""
                <!doctype html>
                <html><head><meta charset="utf-8" /><title>{WebUtility.HtmlEncode(subject)}</title></head>
                <body style="margin:0;background:#f4f4f8;font-family:Segoe UI,Arial,sans-serif;">
                  <div style="max-width:620px;margin:28px auto;background:#fff;border-radius:16px;overflow:hidden;box-shadow:0 12px 32px rgba(20,20,28,.10);">
                    <div style="background:#0ea5e9;color:#fff;padding:20px 26px;">
                      <div style="font-size:1.25rem;font-weight:700;">Geeked On.</div>
                    </div>
                    <div style="padding:24px 26px;">
                      <p style="color:#6b6b7b;margin:0 0 4px;"><strong>To:</strong> {WebUtility.HtmlEncode(toEmail)}</p>
                      <p style="color:#6b6b7b;margin:0 0 16px;"><strong>Subject:</strong> {WebUtility.HtmlEncode(subject)}</p>
                      <hr style="border:none;border-top:1px solid #ececf2;margin:0 0 18px;" />
                      <div style="color:#14141c;font-size:1rem;line-height:1.6;">{linked}</div>
                    </div>
                  </div>
                </body></html>
                """;
        }

        private static string Sanitise(string value) =>
            string.Concat(value.Split(Path.GetInvalidFileNameChars()));

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max];
    }
}
