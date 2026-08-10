using System.Data;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PDFEmailServiceUFMS.Configuration;
using PDFEmailServiceUFMS.Repositories.IRepository;

namespace PDFEmailServiceUFMS.Services;

public class EmailWorkflowService : IEmailWorkflowService
{
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

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Tax & Investment Certificate email workflow");

        if (_appSettings.DryRun)
        {
            _logger.LogWarning("DRY RUN mode is ON - no emails will be sent to anyone");
        }

        // Resolve financial year (from database, falling back to configuration)
        var finYearResult = await _repository.GetFinancialYearAsync(cancellationToken);
        string finYear = finYearResult?.Rows.Count > 0
            ? finYearResult.Rows[0]["FIN_YEAR"]?.ToString() ?? _appSettings.FinancialYear
            : _appSettings.FinancialYear;

        _logger.LogInformation("Using financial year: {FinYear}", finYear);

        var accountEmails = await LoadRecipientsAsync(finYear, cancellationToken);

        // Result columns for the Excel report
        if (!accountEmails.Columns.Contains("Tax"))
            accountEmails.Columns.Add("Tax", typeof(string));
        if (!accountEmails.Columns.Contains("Investment"))
            accountEmails.Columns.Add("Investment", typeof(string));

        _logger.LogInformation("Processing {Count} email recipients", accountEmails.Rows.Count);

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
                _logger.LogWarning("Cancellation requested, stopping workflow");
                break;
            }

            var regBk = accountEmails.Rows[i]["REG_BK"]?.ToString()?.Trim() ?? "";
            var regBr = accountEmails.Rows[i]["REG_BR"]?.ToString()?.Trim() ?? "";
            var regNo = accountEmails.Rows[i]["REG_NO"]?.ToString()?.Trim() ?? "";
            var accountEmail = accountEmails.Rows[i]["EMAIL"]?.ToString()?.Trim() ?? "";
            var cipFlag = accountEmails.Rows[i]["CIP_FLAG"]?.ToString()?.Trim() ?? "N";

            if (string.IsNullOrWhiteSpace(regBk) || string.IsNullOrWhiteSpace(regBr) ||
                string.IsNullOrWhiteSpace(regNo) || string.IsNullOrWhiteSpace(accountEmail))
            {
                _logger.LogWarning("Skipping row {Index} - missing registration or email", i);
                continue;
            }

            try
            {
                var (sent, taxAttached, investAttached) = await ProcessAccountAsync(
                    regBk, regBr, regNo, accountEmail, cipFlag, finYear, pdfOutputPath, cancellationToken);

                accountEmails.Rows[i]["Tax"] = sent && taxAttached ? "Yes" : "No";
                accountEmails.Rows[i]["Investment"] = sent && investAttached ? "Yes" : "No";

                if (sent)
                    successCount++;
                else
                    failureCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process account {RegBk}/{RegBr}/{RegNo} for email {Email}",
                    regBk, regBr, regNo, accountEmail);
                accountEmails.Rows[i]["Tax"] = "No";
                accountEmails.Rows[i]["Investment"] = "No";
                failureCount++;
            }

            // Delay after every batch to prevent SMTP throttling
            if ((i + 1) % _appSettings.EmailBatchSize == 0)
            {
                _logger.LogInformation("Processed {Count} emails, waiting {Minutes} minutes before next batch...",
                    i + 1, _appSettings.BatchDelayMinutes);
                await Task.Delay(TimeSpan.FromMinutes(_appSettings.BatchDelayMinutes), cancellationToken);
            }
        }

        // Export results to Excel
        await ExportToExcelAsync(accountEmails, cancellationToken);

        _logger.LogInformation("Email workflow completed. Success: {Success}, Failures: {Failures}",
            successCount, failureCount);
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

        if (accountEmails == null)
        {
            accountEmails = new DataTable();
            accountEmails.Columns.Add("REG_BK", typeof(string));
            accountEmails.Columns.Add("REG_BR", typeof(string));
            accountEmails.Columns.Add("REG_NO", typeof(string));
            accountEmails.Columns.Add("EMAIL", typeof(string));
            accountEmails.Columns.Add("CIP_FLAG", typeof(string));
        }

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

        var subject = string.Format(_emailSettings.SubjectTemplate, $"{regBk}/{regBr}/{regNo}");
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

    private async Task ExportToExcelAsync(DataTable accountEmails, CancellationToken cancellationToken)
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

            _logger.LogInformation("Excel report saved to {Path}", excelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export Excel report");
        }
    }
}
