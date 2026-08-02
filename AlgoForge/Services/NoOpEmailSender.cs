namespace AlgoForge.Services
{
    // Dev-mode stand-in for a real provider (OPEN-6). Logs instead of sending so the
    // claim/invite flow (D12) is demoable without an SMTP account -- swap for a real
    // sender before anything but a local/demo run.
    public class NoOpEmailSender : IEmailSender
    {
        private readonly ILogger<NoOpEmailSender> _logger;

        public NoOpEmailSender(ILogger<NoOpEmailSender> logger)
        {
            _logger = logger;
        }

        public Task<bool> SendEmailAsync(string toEmail, string subject, string body)
        {
            _logger.LogInformation("Email suppressed (dev mode). To: {Email}, Subject: {Subject}, Body: {Body}", toEmail, subject, body);
            return Task.FromResult(true);
        }
    }
}
