using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using PDFEmailServiceUFMS.Configuration;
using PDFEmailServiceUFMS.Repositories.IRepository;
using Polly;
using Polly.Retry;

namespace PDFEmailServiceUFMS.Services;

public class MailKitEmailService : IEmailService
{
    private readonly EmailSettings _emailSettings;
    private readonly RetryPolicySettings _retrySettings;
    private readonly ILogger<MailKitEmailService> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    public MailKitEmailService(
        IOptions<EmailSettings> emailSettings,
        IOptions<RetryPolicySettings> retrySettings,
        ILogger<MailKitEmailService> logger)
    {
        _emailSettings = emailSettings.Value;
        _retrySettings = retrySettings.Value;
        _logger = logger;
        _retryPipeline = BuildRetryPipeline();
    }

    private ResiliencePipeline BuildRetryPipeline()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _retrySettings.MaxRetryAttempts,
                Delay = TimeSpan.FromSeconds(_retrySettings.InitialDelaySeconds),
                MaxDelay = TimeSpan.FromSeconds(_retrySettings.MaxDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<SmtpCommandException>()
                    .Handle<SmtpProtocolException>()
                    .Handle<IOException>()
                    .Handle<TimeoutException>(),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "Email send attempt {AttemptNumber} failed. Retrying in {Delay}ms...",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task<EmailResult> SendEmailAsync(
        IEnumerable<(byte[] Content, string FileName)> attachments,
        string toAddress,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (!EmailValidator.IsValidEmail(toAddress))
        {
            _logger.LogWarning("Invalid email address: {Email}", toAddress);
            return new EmailResult(false, "Invalid email address");
        }

        try
        {
            await _retryPipeline.ExecuteAsync(async token =>
            {
                await SendEmailInternalAsync(attachments, toAddress, subject, body, token);
            }, cancellationToken);

            _logger.LogInformation("Email sent successfully to {Email}", toAddress);
            return new EmailResult(true, "Success");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email} after all retry attempts", toAddress);
            return new EmailResult(false, $"Failure: {ex.Message}");
        }
    }

    private async Task SendEmailInternalAsync(
        IEnumerable<(byte[] Content, string FileName)> attachments,
        string toAddress,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_emailSettings.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress.Trim()));
        message.Subject = subject;

        var builder = new BodyBuilder
        {
            HtmlBody = body
        };

        foreach (var (content, fileName) in attachments)
        {
            if (content is not null && content.Length > 0)
            {
                builder.Attachments.Add(fileName, content, new ContentType("application", "pdf"));
            }
        }

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();

        var secureSocketOptions = _emailSettings.EnableSsl
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

        await client.ConnectAsync(
            _emailSettings.SmtpServer,
            _emailSettings.Port,
            secureSocketOptions,
            cancellationToken);

        if (!_emailSettings.UseDefaultCredentials && !string.IsNullOrEmpty(_emailSettings.Username))
        {
            await client.AuthenticateAsync(
                _emailSettings.Username,
                _emailSettings.Password,
                cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
