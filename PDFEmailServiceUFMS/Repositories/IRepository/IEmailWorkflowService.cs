namespace PDFEmailServiceUFMS.Repositories.IRepository;

/// <summary>
/// What the UI asked for: which financial year, and either everyone
/// (SendToAll = true) or specific registration(s) (REG_BK / REG_BR / REG_NO).
/// RegNo may be a single number or a comma-separated list ("27112, 50727") —
/// one email is sent per registration number.
/// </summary>
public record WorkflowRequest(
    string FinYear,
    bool SendToAll,
    string? RegBk = null,
    string? RegBr = null,
    string? RegNo = null);

public record WorkflowResult(int TotalRecipients, int SuccessCount, int FailureCount);

public interface IEmailWorkflowService
{
    /// <summary>
    /// Runs the certificate email workflow for the given request.
    /// Progress messages (per-recipient status, batch waits, report path) are
    /// reported through <paramref name="progress"/> for display on the UI.
    /// </summary>
    Task<WorkflowResult> ExecuteAsync(WorkflowRequest request, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
