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

        // Never throws. Everything below -- missing configuration, a refused connection,
        // rejected credentials, a provider rate-limiting us -- is logged and reported as
        // false. An invite that could not be emailed is a thing the coordinator needs to
        // be told about and work around, not an error page in the middle of running an
        // event, and certainly not a "sent" message that never left the building.
        public async Task<bool> SendEmailAsync(
            string toEmail,
            string subject,
            string body)
        {
            var host = _configuration["Smtp:Host"];
            var portText = _configuration["Smtp:Port"];
            var username = _configuration["Smtp:Username"];
            var password = _configuration["Smtp:Password"];
            var from = _configuration["Smtp:From"] ?? username;

            var missing = new[]
            {
                host is null ? "Smtp:Host" : null,
                portText is null ? "Smtp:Port" : null,
                username is null ? "Smtp:Username" : null,
                password is null ? "Smtp:Password" : null
            }.Where(k => k is not null).ToArray();

            if (missing.Length > 0)
            {
                _logger.LogError(
                    "Cannot send mail to {Email}: missing configuration {Keys}.",
                    toEmail, string.Join(", ", missing));

                return false;
            }

            if (!int.TryParse(portText, out var port))
            {
                _logger.LogError(
                    "Cannot send mail to {Email}: Smtp:Port '{Port}' is not a number.",
                    toEmail, portText);

                return false;
            }

            try
            {
                return await SendCoreAsync(toEmail, subject, body, host!, port, username!, password!, from!);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex, "Failed to send mail to {Email} via {Host}:{Port}.", toEmail, host, port);

                return false;
            }
        }

        private async Task<bool> SendCoreAsync(
            string toEmail,
            string subject,
            string body,
            string host,
            int port,
            string username,
            string password,
            string from)
        {
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

            await client.ConnectAsync(
                host,
                port,
                SecureSocketOptions.StartTls);

            await client.AuthenticateAsync(
                username,
                password);

            await client.SendAsync(message);

            await client.DisconnectAsync(true);

            _logger.LogInformation(
                "Email sent to {Email}: {Subject}",
                toEmail,
                subject);

            return true;
        }
    }
}
