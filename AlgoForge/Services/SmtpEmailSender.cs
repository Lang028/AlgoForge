using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AlgoForge.Services
{
    // Real email, for anything past a local/demo run -- see OutboxEmailSender for that side.
    // Settings come from configuration rather than being hardcoded so the same code works for
    // Gmail, Outlook, or any other SMTP account: only appsettings/user-secrets/environment
    // variables change between them, never this file.
    public class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpEmailOptions _options;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IOptions<SmtpEmailOptions> options, ILogger<SmtpEmailSender> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                string.IsNullOrWhiteSpace(_options.FromName) ? "Geeked On" : _options.FromName,
                _options.Username));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            // Callers pass plain text (invite links, tokens); TextPart handles the encoding,
            // so nothing here needs to think about HTML-escaping a URL or a name.
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();
            await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_options.Username, _options.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent. To: {Email}, Subject: {Subject}", toEmail, subject);
        }
    }

    // Bound from the "Email" configuration section. Host/Port/FromName are fine in
    // appsettings.json; Username/Password are secrets and belong in user-secrets locally or
    // the hosting platform's environment/configuration in production -- never committed.
    public class SmtpEmailOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? FromName { get; set; }
    }
}
