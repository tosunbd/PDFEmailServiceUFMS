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

/// <summary>
/// SMTP sender for one run.
///
/// The BCC relay (smtp.bcc.gov.bd, a Postfix pool that answers as nemta*/smmta*.bcc.gov.bd)
/// rate-limits per client. Opening a fresh TCP connection + STARTTLS + AUTH for every single
/// message trips Postfix's anvil limiter, which then answers "4.7.1 Service unavailable - try
/// again later", rejects a perfectly correct login with "535 5.7.8 authentication failed", or
/// just drops the connection during AUTH. The next message often succeeds, which is why the
/// failures look random and account-specific when they are not.
///
/// So ONE authenticated connection is opened per run and reused for every message. It is only
/// re-opened when the relay actually drops it.
/// </summary>
public class MailKitEmailService : IEmailService, IDisposable, IAsyncDisposable
{
    private readonly EmailSettings _emailSettings;
    private readonly RetryPolicySettings _retrySettings;
    private readonly ILogger<MailKitEmailService> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    // One connection shared by every message of the run, guarded because Polly can resume a
    // retry on a different thread pool thread.
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private SmtpClient? _client;
    private DateTimeOffset _lastSendUtc = DateTimeOffset.MinValue;
    private bool _disposed;

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
                    // The relay answers "535 authentication failed" to a correct login while it
                    // is throttling, so a rejected login is transient here, not fatal.
                    .Handle<AuthenticationException>()
                    .Handle<System.Net.Sockets.SocketException>()
                    .Handle<IOException>()
                    .Handle<TimeoutException>()
                    // MailKit surfaces its own socket timeout as a cancelled task. Retry that,
                    // but never the operator pressing Cancel — Polly stops on its own once the
                    // caller's token is cancelled.
                    .Handle<OperationCanceledException>(),
                OnRetry = async args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "Email send attempt {AttemptNumber} failed. Retrying in {Delay}ms...",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds);

                    // The connection is unusable after any of these errors, so drop it and let
                    // the next attempt log in again rather than retry down a dead socket.
                    await DropConnectionAsync();
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The operator pressed Cancel: let the workflow stop instead of recording a failure.
            throw;
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

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            await PaceAsync(cancellationToken);
            var client = await EnsureConnectedAsync(cancellationToken);
            await client.SendAsync(message, cancellationToken);
            _lastSendUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Keeps a minimum gap between two messages on the same connection. Postfix limits messages
    /// per unit time as well as connections, so firing them back to back re-trips the limiter
    /// even on a single connection.
    /// </summary>
    private async Task PaceAsync(CancellationToken cancellationToken)
    {
        if (_emailSettings.DelayBetweenEmailsMs <= 0 || _lastSendUtc == DateTimeOffset.MinValue)
        {
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - _lastSendUtc;
        var gap = TimeSpan.FromMilliseconds(_emailSettings.DelayBetweenEmailsMs) - elapsed;
        if (gap > TimeSpan.Zero)
        {
            await Task.Delay(gap, cancellationToken);
        }
    }

    /// <summary>
    /// Returns the live authenticated connection, opening one only when there is none or the
    /// relay has dropped the previous one.
    /// </summary>
    private async Task<SmtpClient> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client is { IsConnected: true } existing &&
            (existing.IsAuthenticated || !RequiresAuthentication))
        {
            return existing;
        }

        await DisposeClientAsync();

        var client = new SmtpClient
        {
            Timeout = _emailSettings.SmtpTimeoutSeconds * 1000
        };

        var secureSocketOptions = _emailSettings.EnableSsl
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

        await client.ConnectAsync(
            _emailSettings.SmtpServer,
            _emailSettings.Port,
            secureSocketOptions,
            cancellationToken);

        if (RequiresAuthentication)
        {
            await client.AuthenticateAsync(
                _emailSettings.Username,
                _emailSettings.Password,
                cancellationToken);
        }

        _logger.LogInformation("Opened SMTP connection to {Server}:{Port}",
            _emailSettings.SmtpServer, _emailSettings.Port);

        _client = client;
        return client;
    }

    private bool RequiresAuthentication =>
        !_emailSettings.UseDefaultCredentials && !string.IsNullOrEmpty(_emailSettings.Username);

    /// <summary>Closes the current connection so the next send logs in again.</summary>
    private async Task DropConnectionAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            await DisposeClientAsync();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task DisposeClientAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync(true, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ignoring error while closing the SMTP connection");
        }
        finally
        {
            _client.Dispose();
            _client = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        await DisposeClientAsync();
        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        try
        {
            if (_client is { IsConnected: true })
            {
                _client.Disconnect(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ignoring error while closing the SMTP connection");
        }
        finally
        {
            _client?.Dispose();
            _client = null;
            _connectionLock.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
