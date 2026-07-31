using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AlgoForge.Services
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(
            IConfiguration configuration,
            ILogger<SmtpEmailSender> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendEmailAsync(
            string toEmail,
            string subject,
            string body)
        {
            // TEST MESSAGE:
            // This lets us confirm that the application is actually reaching
            // SmtpEmailSender when an invite is sent.
            _logger.LogInformation(
                "SMTP EMAIL SENDER CALLED for {Email}",
                toEmail);

            var host = _configuration["Smtp:Host"]
                ?? throw new InvalidOperationException(
                    "Smtp:Host is missing.");

            var portText = _configuration["Smtp:Port"]
                ?? throw new InvalidOperationException(
                    "Smtp:Port is missing.");

            var username = _configuration["Smtp:Username"]
                ?? throw new InvalidOperationException(
                    "Smtp:Username is missing.");

            var password = _configuration["Smtp:Password"]
                ?? throw new InvalidOperationException(
                    "Smtp:Password is missing.");

            var from = _configuration["Smtp:From"]
                ?? username;

            if (!int.TryParse(portText, out var port))
            {
                throw new InvalidOperationException(
                    "Smtp:Port must be a valid number.");
            }

            var message = new MimeMessage();

            message.From.Add(
                MailboxAddress.Parse(from));

            message.To.Add(
                MailboxAddress.Parse(toEmail));

            message.Subject = subject;

            message.Body = new TextPart("plain")
            {
                Text = body
            };

            using var client = new SmtpClient();

            _logger.LogInformation(
                "Connecting to SMTP server {Host}:{Port}",
                host,
                port);

            await client.ConnectAsync(
                host,
                port,
                SecureSocketOptions.StartTls);

            _logger.LogInformation(
                "SMTP connection successful. Authenticating as {Username}",
                username);

            await client.AuthenticateAsync(
                username,
                password);

            _logger.LogInformation(
                "SMTP authentication successful. Sending email to {Email}",
                toEmail);

            await client.SendAsync(message);

            await client.DisconnectAsync(true);

            _logger.LogInformation(
                "Email sent successfully to {Email}",
                toEmail);
        }
    }
}
