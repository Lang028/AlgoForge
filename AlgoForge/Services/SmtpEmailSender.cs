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

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var host = _configuration["Smtp:Host"]
                ?? throw new InvalidOperationException("Smtp:Host is missing.");

            var portText = _configuration["Smtp:Port"]
                ?? throw new InvalidOperationException("Smtp:Port is missing.");

            var username = _configuration["Smtp:Username"]
                ?? throw new InvalidOperationException("Smtp:Username is missing.");

            var password = _configuration["Smtp:Password"]
                ?? throw new InvalidOperationException("Smtp:Password is missing.");

            var from = _configuration["Smtp:From"]
                ?? username;

            if (!int.TryParse(portText, out var port))
                throw new InvalidOperationException("Smtp:Port must be a number.");

            var message = new MimeMessage();

            message.From.Add(MailboxAddress.Parse(from));
            message.To.Add(MailboxAddress.Parse(toEmail));
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

            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation(
                "Email sent successfully. To: {Email}, Subject: {Subject}",
                toEmail,
                subject);
        }
    }
}