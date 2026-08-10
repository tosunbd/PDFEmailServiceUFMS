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
