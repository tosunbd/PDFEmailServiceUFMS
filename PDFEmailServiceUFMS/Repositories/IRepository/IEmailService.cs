namespace PDFEmailServiceUFMS.Repositories.IRepository;

public interface IEmailService
{
    Task<EmailResult> SendEmailAsync(
        IEnumerable<(byte[] Content, string FileName)> attachments,
        string toAddress,
        string subject,
        string body,
        CancellationToken cancellationToken = default);
}

public record EmailResult(bool Success, string Message);
