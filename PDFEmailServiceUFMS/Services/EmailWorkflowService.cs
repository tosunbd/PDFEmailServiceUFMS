using System.Data;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PDFEmailServiceUFMS.Configuration;
using PDFEmailServiceUFMS.Repositories.IRepository;

namespace PDFEmailServiceUFMS.Services;

public class EmailWorkflowService : IEmailWorkflowService
{
    // ╔════════════════════════════════════════════════════════════════════════════╗
    // ║ >>> TEST-OVERRIDE — CHANGE HERE FOR TESTING <<<        (search: TEST-OVERRIDE)
    // ║
    // ║ TEST MODE  : comment out the "= null;" line and uncomment the block below
    // ║              it. Then EVERY email is redirected to the address given here,
    // ║              and ONLY the given registration is processed (even if you
    // ║              tick "Send to ALL" on the UI).
    // ║ NORMAL MODE: keep "= null;" active and comment the test block back out —
    // ║              the normal sending loop runs and emails go to the real
    // ║              unit-holder addresses from the database.
    // ╚════════════════════════════════════════════════════════════════════════════╝
    private static readonly TestOverride? ActiveTestOverride = null;
    //private static readonly TestOverride? ActiveTestOverride = new(
    //    Email: "tosuniscool@gmail.com",   // ← every email goes ONLY to this address
    //    RegBk: "ICB",                 // ← only this registration is processed
    //    RegBr: "1",
    //    RegNo: "59007");

    // Note: RegNo may also be a comma-separated list, e.g. "27112, 50727, 59005" —
    // one email is then sent per registration, all redirected to the test address.
    private sealed record TestOverride(string Email, string? RegBk = null, string? RegBr = null, string? RegNo = null);

    /// <summary>Lets the UI show a warning banner while the TEST-OVERRIDE is active.</summary>
    internal static string? TestOverrideDescription => ActiveTestOverride is null
        ? null
        : $"{ActiveTestOverride.Email}" +
          (ActiveTestOverride.RegNo is null ? "" : $" (registration {ActiveTestOverride.RegBk}/{ActiveTestOverride.RegBr}/{ActiveTestOverride.RegNo} only)");

    private readonly IUnitFundRepository _repository;
    private readonly IPdfGenerationService _pdfService;
    private readonly IEmailService _emailService;
    private readonly ApplicationSettings _appSettings;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<EmailWorkflowService> _logger;

    public EmailWorkflowService(
        IUnitFundRepository repository,
        IPdfGenerationService pdfService,
        IEmailService emailService,
        IOptions<ApplicationSettings> appSettings,
        IOptions<EmailSettings> emailSettings,
        ILogger<EmailWorkflowService> logger)
    {
        _repository = repository;
        _pdfService = pdfService;
        _emailService = emailService;
        _appSettings = appSettings.Value;
        _emailSettings = emailSettings.Value;
        _logger = logger;
    }

    public async Task<WorkflowResult> ExecuteAsync(WorkflowRequest request, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        void Report(string message)
        {
            _logger.LogInformation("{Message}", message);
            progress?.Report(message);
        }

        Report("Starting Tax & Investment Certificate email workflow");

        if (_appSettings.DryRun)
        {
            Report("DRY RUN mode is ON - no emails will be sent to anyone");
        }

        if (ActiveTestOverride is not null)
        {
            Report($"*** TEST-OVERRIDE ACTIVE *** all emails redirected to {ActiveTestOverride.Email}");
            if (ActiveTestOverride is { RegBk: not null, RegBr: not null, RegNo: not null })
            {
                Report($"*** TEST-OVERRIDE *** only registration {ActiveTestOverride.RegBk}/{ActiveTestOverride.RegBr}/{ActiveTestOverride.RegNo} will be processed");
                request = new WorkflowRequest(request.FinYear, SendToAll: false,
                    ActiveTestOverride.RegBk, ActiveTestOverride.RegBr, ActiveTestOverride.RegNo);
            }
        }

        // Resolve financial year (from the request; falls back to DB max, then configuration)
        var finYear = request.FinYear;
        if (string.IsNullOrWhiteSpace(finYear))
        {
            var finYearResult = await _repository.GetFinancialYearAsync(cancellationToken);
            finYear = finYearResult?.Rows.Count > 0
                ? finYearResult.Rows[0]["FIN_YEAR"]?.ToString() ?? _appSettings.FinancialYear
                : _appSettings.FinancialYear;
        }

        Report($"Using financial year: {finYear}");

        var accountEmails = request.SendToAll
            ? await LoadRecipientsAsync(finYear, cancellationToken)
            : await LoadSpecificRecipientsAsync(request, finYear, Report, cancellationToken);

        // Result columns for the Excel report and the printable sent-status log
        if (!accountEmails.Columns.Contains("Tax"))
            accountEmails.Columns.Add("Tax", typeof(string));
        if (!accountEmails.Columns.Contains("Investment"))
            accountEmails.Columns.Add("Investment", typeof(string));
        if (!accountEmails.Columns.Contains("Status"))
            accountEmails.Columns.Add("Status", typeof(string));
        if (!accountEmails.Columns.Contains("SENT_EMAIL"))
            accountEmails.Columns.Add("SENT_EMAIL", typeof(string));

        var totalRecipients = accountEmails.Rows.Count;
        Report($"Processing {totalRecipients} email recipient(s)");

        if (!request.SendToAll && totalRecipients == 0)
        {
            Report($"No recipient found for registration(s) {request.RegBk}/{request.RegBr}/{request.RegNo} " +
                   $"in {finYear} (needs NET_DIVIDENT > 0 for that year)");
            return new WorkflowResult(0, 0, 0);
        }

        var currentPath = AppContext.BaseDirectory;
        var pdfOutputPath = _appSettings.PdfOutputPath;
        if (!Path.IsPathRooted(pdfOutputPath))
        {
            pdfOutputPath = Path.Combine(currentPath, pdfOutputPath);
        }
        Directory.CreateDirectory(pdfOutputPath);

        var logOutputPath = Path.Combine(currentPath, _appSettings.LogOutputPath);
        Directory.CreateDirectory(logOutputPath);

        var successCount = 0;
        var failureCount = 0;

        for (int i = 0; i < accountEmails.Rows.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Report("Cancellation requested, stopping workflow");
                break;
            }

            var regBk = accountEmails.Rows[i]["REG_BK"]?.ToString()?.Trim() ?? "";
            var regBr = accountEmails.Rows[i]["REG_BR"]?.ToString()?.Trim() ?? "";
            var regNo = accountEmails.Rows[i]["REG_NO"]?.ToString()?.Trim() ?? "";
            var accountEmail = accountEmails.Rows[i]["EMAIL"]?.ToString()?.Trim() ?? "";
            var cipFlag = accountEmails.Rows[i]["CIP_FLAG"]?.ToString()?.Trim() ?? "N";

            // ── TEST-OVERRIDE: redirect the "to" address ──────────────────────────
            if (ActiveTestOverride is not null)
            {
                accountEmail = ActiveTestOverride.Email;
            }
            // ──────────────────────────────────────────────────────────────────────

            accountEmails.Rows[i]["SENT_EMAIL"] = accountEmail;
            accountEmails.Rows[i]["Status"] = "NO";

            if (string.IsNullOrWhiteSpace(regBk) || string.IsNullOrWhiteSpace(regBr) ||
                string.IsNullOrWhiteSpace(regNo) || string.IsNullOrWhiteSpace(accountEmail))
            {
                Report($"Skipping row {i} - missing registration or email");
                accountEmails.Rows[i]["Tax"] = "No";
                accountEmails.Rows[i]["Investment"] = "No";
                continue;
            }

            try
            {
                var (sent, taxAttached, investAttached) = await ProcessAccountAsync(
                    regBk, regBr, regNo, accountEmail, cipFlag, finYear, pdfOutputPath, cancellationToken);

                accountEmails.Rows[i]["Tax"] = sent && taxAttached ? "Yes" : "No";
                accountEmails.Rows[i]["Investment"] = sent && investAttached ? "Yes" : "No";
                accountEmails.Rows[i]["Status"] = sent ? "YES" : "NO";

                if (sent)
                {
                    successCount++;
                    Report($"[{i + 1}/{totalRecipients}] {regBk}/{regBr}/{regNo} → {accountEmail} : " +
                           $"SENT (Tax={(taxAttached ? "Yes" : "No")}, Investment={(investAttached ? "Yes" : "No")})");
                }
                else
                {
                    failureCount++;
                    Report($"[{i + 1}/{totalRecipients}] {regBk}/{regBr}/{regNo} → {accountEmail} : NOT SENT");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process account {RegBk}/{RegBr}/{RegNo} for email {Email}",
                    regBk, regBr, regNo, accountEmail);
                progress?.Report($"[{i + 1}/{totalRecipients}] {regBk}/{regBr}/{regNo} → {accountEmail} : ERROR - {ex.Message}");
                accountEmails.Rows[i]["Tax"] = "No";
                accountEmails.Rows[i]["Investment"] = "No";
                failureCount++;
            }

            // Delay after every batch to prevent SMTP throttling
            if ((i + 1) % _appSettings.EmailBatchSize == 0 && i + 1 < accountEmails.Rows.Count)
            {
                Report($"Processed {i + 1} emails, waiting {_appSettings.BatchDelayMinutes} minutes before next batch...");
                await Task.Delay(TimeSpan.FromMinutes(_appSettings.BatchDelayMinutes), cancellationToken);
            }
        }

        // Export results to Excel
        await ExportToExcelAsync(accountEmails, Report, cancellationToken);

        // Printable sent-status log for the admin
        await WriteSentLogAsync(accountEmails, logOutputPath, finYear, Report);

        Report($"Email workflow completed. Success: {successCount}, Failures: {failureCount}");

        return new WorkflowResult(totalRecipients, successCount, failureCount);
    }

    private async Task<DataTable> LoadRecipientsAsync(string finYear, CancellationToken cancellationToken)
    {
        DataTable? accountEmails = null;

        if (_appSettings.SkipDatabaseRecipients)
        {
            _logger.LogWarning("SkipDatabaseRecipients is ON - only TestAccounts will be processed");
        }
        else
        {
            accountEmails = await _repository.GetAccountEmailAsync(finYear, cancellationToken);
        }

        accountEmails ??= CreateEmptyRecipientTable();

        // Add test accounts from configuration (CIP_FLAG = 'Y' so both certificates are attempted)
        foreach (var testAccount in _appSettings.TestAccounts)
        {
            var newRow = accountEmails.NewRow();
            newRow["REG_BK"] = testAccount.RegBk;
            newRow["REG_BR"] = testAccount.RegBr;
            newRow["REG_NO"] = testAccount.RegNo;
            newRow["EMAIL"] = testAccount.Email;
            newRow["CIP_FLAG"] = "Y";
            accountEmails.Rows.Add(newRow);
        }

        // Remove rows with null values
        for (int i = accountEmails.Rows.Count - 1; i >= 0; i--)
        {
            var row = accountEmails.Rows[i];
            if (row["REG_BK"] == DBNull.Value || row["REG_BR"] == DBNull.Value ||
                row["REG_NO"] == DBNull.Value || row["EMAIL"] == DBNull.Value)
            {
                accountEmails.Rows[i].Delete();
            }
        }
        accountEmails.AcceptChanges();

        return accountEmails;
    }

    /// <summary>
    /// "Specific registration" mode: no TestAccounts appended. RegNo may be a single
    /// number or a comma-separated list (e.g. "27112, 50727, 59005") — one email is
    /// sent per registration number, in the order given.
    /// </summary>
    private async Task<DataTable> LoadSpecificRecipientsAsync(WorkflowRequest request, string finYear, Action<string> report, CancellationToken cancellationToken)
    {
        var table = CreateEmptyRecipientTable();

        var regNos = (request.RegNo ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();

        foreach (var regNo in regNos)
        {
            var result = await _repository.GetAccountEmailByRegistrationAsync(
                request.RegBk ?? "", request.RegBr ?? "", regNo, finYear, cancellationToken);

            if (result is { Rows.Count: > 0 })
            {
                foreach (DataRow row in result.Rows)
                {
                    var newRow = table.NewRow();
                    newRow["REG_BK"] = row["REG_BK"]?.ToString();
                    newRow["REG_BR"] = row["REG_BR"]?.ToString();
                    newRow["REG_NO"] = row["REG_NO"]?.ToString();
                    newRow["EMAIL"] = row["EMAIL"] == DBNull.Value ? DBNull.Value : row["EMAIL"]?.ToString();
                    newRow["CIP_FLAG"] = row["CIP_FLAG"]?.ToString();
                    table.Rows.Add(newRow);
                }
            }
            else
            {
                report($"No recipient found for registration {request.RegBk}/{request.RegBr}/{regNo} " +
                       $"in {finYear} (needs NET_DIVIDENT > 0 for that year) - skipped");
            }
        }

        return table;
    }

    private static DataTable CreateEmptyRecipientTable()
    {
        var table = new DataTable();
        table.Columns.Add("REG_BK", typeof(string));
        table.Columns.Add("REG_BR", typeof(string));
        table.Columns.Add("REG_NO", typeof(string));
        table.Columns.Add("EMAIL", typeof(string));
        table.Columns.Add("CIP_FLAG", typeof(string));
        return table;
    }

    /// <returns>
    /// Sent: true when the email was sent (or dry-run simulated) successfully.
    /// TaxAttached / InvestAttached: which certificates were generated and attached.
    /// </returns>
    private async Task<(bool Sent, bool TaxAttached, bool InvestAttached)> ProcessAccountAsync(
        string regBk,
        string regBr,
        string regNo,
        string email,
        string cipFlag,
        string finYear,
        string pdfOutputPath,
        CancellationToken cancellationToken)
    {
        string registrationNo = $"{regBk}_{regBr}_{regNo}";
        _logger.LogInformation("Processing registration {RegistrationNo} for email {Email}", registrationNo, email);

        var attachments = new List<(byte[] Content, string FileName)>();

        // 1. Income Tax Certificate (all dividend recipients)
        var taxPdf = await _pdfService.GenerateIncomeTaxPdfAsync(regBk, regBr, regNo, finYear, cancellationToken);
        if (taxPdf.Length > 0)
        {
            var taxFileName = $"{registrationNo}_Tax_Certificate.pdf";
            attachments.Add((taxPdf, taxFileName));

            var taxFilePath = Path.Combine(pdfOutputPath, taxFileName);
            await File.WriteAllBytesAsync(taxFilePath, taxPdf, cancellationToken);
            _logger.LogDebug("Tax certificate PDF saved to {Path}", taxFilePath);
        }
        else
        {
            _logger.LogWarning("No tax certificate PDF generated for registration {RegistrationNo}", registrationNo);
        }

        // 2. Investment Certificate (only holders who reinvested dividends as CIP units)
        var investPdf = Array.Empty<byte>();
        if (string.Equals(cipFlag, "Y", StringComparison.OrdinalIgnoreCase))
        {
            investPdf = await _pdfService.GenerateInvestmentCertificatePdfAsync(regBk, regBr, regNo, finYear, cancellationToken);
            if (investPdf.Length > 0)
            {
                var investFileName = $"{registrationNo}_Investment_Certificate.pdf";
                attachments.Add((investPdf, investFileName));

                var investFilePath = Path.Combine(pdfOutputPath, investFileName);
                await File.WriteAllBytesAsync(investFilePath, investPdf, cancellationToken);
                _logger.LogDebug("Investment certificate PDF saved to {Path}", investFilePath);
            }
            else
            {
                _logger.LogWarning("No investment certificate PDF generated for registration {RegistrationNo}", registrationNo);
            }
        }

        if (attachments.Count == 0)
        {
            _logger.LogWarning("No PDFs generated for registration {RegistrationNo} - email skipped", registrationNo);
            return (false, false, false);
        }

        // Subject reflects what is actually attached:
        // both -> "Income Tax & Investment Certificate...", tax only -> "Income Tax Certificate...", etc.
        var subjectTemplate = (taxPdf.Length > 0, investPdf.Length > 0) switch
        {
            (true, true) => _emailSettings.SubjectTemplate,
            (true, false) => _emailSettings.SubjectTemplateTaxOnly,
            (false, true) => _emailSettings.SubjectTemplateInvestmentOnly,
            _ => _emailSettings.SubjectTemplate
        };
        var subject = string.Format(subjectTemplate, $"{regBk}/{regBr}/{regNo}");
        var body = _emailSettings.BodyTemplate;

        if (_appSettings.DryRun)
        {
            _logger.LogInformation("[DRY RUN] Email to {Email} for registration {RegistrationNo} suppressed (attachments: {Count})",
                email, registrationNo, attachments.Count);
            return (true, taxPdf.Length > 0, investPdf.Length > 0);
        }

        var result = await _emailService.SendEmailAsync(
            attachments,
            email,
            subject,
            body,
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation("Email sent successfully to {Email} for registration {RegistrationNo}",
                email, registrationNo);
        }
        else
        {
            _logger.LogWarning("Failed to send email to {Email}: {Message}", email, result.Message);
        }

        return (result.Success, taxPdf.Length > 0, investPdf.Length > 0);
    }

    /// <summary>
    /// Writes the printable per-run report logs/ufmsTaxInvestEmailSent_*.txt:
    /// one line per registration showing which certificates were attached, whether
    /// the email was sent, and the address it was sent to. Written even after a
    /// cancellation so partial runs can still be reported to the admin.
    /// </summary>
    private async Task WriteSentLogAsync(DataTable accountEmails, string logOutputPath, string finYear, Action<string> report)
    {
        try
        {
            var path = Path.Combine(logOutputPath, $"ufmsTaxInvestEmailSent_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            var lines = new List<string>
            {
                "ICB Unit Fund Department - Income Tax & Investment Certificate Email Report",
                $"Run at         : {DateTime.Now:dd-MMM-yyyy HH:mm:ss}",
                $"Financial Year : {finYear}"
            };
            if (_appSettings.DryRun)
                lines.Add("NOTE: DRY RUN was ON - no email was actually sent.");
            if (ActiveTestOverride is not null)
                lines.Add($"NOTE: TEST-OVERRIDE was ON - all emails were redirected to {ActiveTestOverride.Email}.");

            var separator = new string('-', 110);
            lines.Add(separator);
            lines.Add($"{"REG_BK",-8}{"REG_BR",-8}{"REG_NO",-10}{"TAX_CERT",-10}{"INVESTMENT_CERT",-17}{"STATUS(SENT)",-14}EMAIL");
            lines.Add(separator);

            int sentCount = 0, notSentCount = 0;
            foreach (DataRow row in accountEmails.Rows)
            {
                var regBk = row["REG_BK"]?.ToString()?.Trim() ?? "";
                var regBr = row["REG_BR"]?.ToString()?.Trim() ?? "";
                var regNo = row["REG_NO"]?.ToString()?.Trim() ?? "";
                var tax = (row["Tax"]?.ToString() ?? "No").ToUpperInvariant();
                var invest = (row["Investment"]?.ToString() ?? "No").ToUpperInvariant();
                var status = string.IsNullOrWhiteSpace(row["Status"]?.ToString()) ? "NO" : row["Status"]!.ToString()!;
                var email = row["SENT_EMAIL"]?.ToString();
                if (string.IsNullOrWhiteSpace(email))
                    email = row["EMAIL"]?.ToString()?.Trim() ?? "";

                if (status == "YES") sentCount++; else notSentCount++;

                lines.Add($"{regBk,-8}{regBr,-8}{regNo,-10}{tax,-10}{invest,-17}{status,-14}{email}");
            }

            lines.Add(separator);
            lines.Add($"Total: {accountEmails.Rows.Count}    Sent: {sentCount}    Not sent: {notSentCount}");

            // CancellationToken.None: this report must be written even when the run was cancelled
            await File.WriteAllLinesAsync(path, lines, CancellationToken.None);

            report($"Sent-status log saved to {path}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write sent-status log");
        }
    }

    private async Task ExportToExcelAsync(DataTable accountEmails, Action<string> report, CancellationToken cancellationToken)
    {
        try
        {
            var currentPath = AppContext.BaseDirectory;
            var excelFolder = Path.Combine(currentPath, _appSettings.ExcelOutputPath);
            Directory.CreateDirectory(excelFolder);

            var excelPath = Path.Combine(excelFolder, $"EmailReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Emails");
            worksheet.Cell(1, 1).InsertTable(accountEmails);

            await Task.Run(() => workbook.SaveAs(excelPath), cancellationToken);

            report($"Excel report saved to {excelPath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export Excel report");
        }
    }
}
