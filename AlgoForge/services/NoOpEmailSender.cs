using Microsoft.AspNetCore.Identity.UI.Services;

namespace AlgoForge.Services
{
    // Placeholder email sender for development. Logs instead of sending.
    // Swap this for a real provider (SendGrid, SMTP, etc.) before deploying.
    public class NoOpEmailSender : IEmailSender
    {
        private readonly ILogger<NoOpEmailSender> _logger;

        public NoOpEmailSender(ILogger<NoOpEmailSender> logger)
        {
            _logger = logger;
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            _logger.LogInformation("Email suppressed (dev mode). To: {Email}, Subject: {Subject}", email, subject);
            return Task.CompletedTask;
        }
    }
}
