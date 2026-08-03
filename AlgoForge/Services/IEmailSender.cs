namespace AlgoForge.Services
{
    public interface IEmailSender
    {
        // Returns false when the message could not be handed to the mail server, rather
        // than throwing. A provider being down, rejecting credentials or rate-limiting is
        // an ordinary operational event: it must not turn a coordinator's invite into an
        // error page, and it must not be reported to them as a message that was sent.
        // Callers decide what a failure means for the action they were performing.
        Task<bool> SendEmailAsync(string toEmail, string subject, string body);
    }
}
