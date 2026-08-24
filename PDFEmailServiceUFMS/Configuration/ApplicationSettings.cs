namespace PDFEmailServiceUFMS.Configuration;

public class ApplicationSettings
{
    public int BranchCode { get; set; } = 1;
    public string PdfOutputPath { get; set; } = "PDF";
    public string LogOutputPath { get; set; } = "logs";
    public string ExcelOutputPath { get; set; } = "Excel";
    public string FinancialYear { get; set; } = "2024-2025";
    public int EmailBatchSize { get; set; } = 300;
    public int BatchDelayMinutes { get; set; } = 3;
    public List<TestAccount> TestAccounts { get; set; } = new();

    /// <summary>
    /// Memo number printed at the top left of a certificate. A {FIN_YEAR}
    /// placeholder is replaced with the financial year of the run. Mirrors the
    /// CertificateLetterNo keys in the UFMS Web.config, so a number changed
    /// there should be changed here too.
    /// </summary>
    public string CertificateLetterNo { get; set; } = "53.13.0000.000.018.56.0001.26";

    /// <summary>Overrides <see cref="CertificateLetterNo"/> on the Income Tax Certificate.</summary>
    public string CertificateLetterNoIncomeTax { get; set; } = "";

    /// <summary>Overrides <see cref="CertificateLetterNo"/> on the Investment Certificate.</summary>
    public string CertificateLetterNoInvestment { get; set; } = "53.13.0000,000,018.56.0002.26";

    /// <summary>
    /// When true, everything runs normally (queries, PDF generation, Excel report)
    /// but NO email is sent to anyone. Use for safe testing.
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// When true, recipients fetched from the database are skipped and only
    /// TestAccounts are processed. Use for safe testing without emailing unit holders.
    /// </summary>
    public bool SkipDatabaseRecipients { get; set; } = false;
}

public class TestAccount
{
    public string RegBk { get; set; } = string.Empty;
    public string RegBr { get; set; } = string.Empty;
    public string RegNo { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
