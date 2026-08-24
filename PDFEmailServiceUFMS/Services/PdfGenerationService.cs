using System.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PDFEmailServiceUFMS.Configuration;
using PDFEmailServiceUFMS.Models;
using PDFEmailServiceUFMS.Repositories.IRepository;
using QuestPDF.Infrastructure;

namespace PDFEmailServiceUFMS.Services;

/// <summary>
/// Fetches the certificate data for a registration and hands it to
/// <see cref="CertificatePdfBuilder"/>, which holds the layout ported from the
/// UFMS unitf059v1 screen. The per-year parts of a certificate — the rule
/// wording from UNIT_PARAMETERS and the challans of the tax payment table —
/// are the same for every recipient of a run, so they are read once and cached
/// for the lifetime of this (scoped) service.
/// </summary>
public class PdfGenerationService : IPdfGenerationService
{
    private readonly IUnitFundRepository _repository;
    private readonly ApplicationSettings _appSettings;
    private readonly ILogger<PdfGenerationService> _logger;

    // The two certificates of one account are generated back to back from the
    // same row, so the last one read is kept to save the second round trip.
    private string _lastDataKey = "";
    private HolderCertificateInfo? _lastData;

    private readonly Dictionary<string, string> _incomeTaxRuleCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _investmentRuleCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ChallanInfo>> _challanCache = new(StringComparer.OrdinalIgnoreCase);

    static PdfGenerationService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public PdfGenerationService(
        IUnitFundRepository repository,
        IOptions<ApplicationSettings> appSettings,
        ILogger<PdfGenerationService> logger)
    {
        _repository = repository;
        _appSettings = appSettings.Value;
        _logger = logger;
    }

    public async Task<byte[]> GenerateIncomeTaxPdfAsync(
        string regBk,
        string regBr,
        string regNo,
        string finYear,
        CancellationToken cancellationToken = default)
    {
        if (!HasCompleteRegistration(regBk, regBr, regNo))
            return Array.Empty<byte>();

        try
        {
            var data = await LoadCertificateDataAsync(regBk, regBr, regNo, finYear, cancellationToken);
            if (data == null)
            {
                _logger.LogWarning("No dividend data found for {RegBk}/{RegBr}/{RegNo}", regBk, regBr, regNo);
                return Array.Empty<byte>();
            }

            var ruleText = await GetIncomeTaxRuleTextAsync(finYear, cancellationToken);
            var challans = await GetChallansAsync(finYear, cancellationToken);

            return RenderCertificate(CertificatePdfBuilder.IncomeTaxType, data, ruleText, finYear, challans);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating income tax PDF for {RegBk}/{RegBr}/{RegNo}", regBk, regBr, regNo);
            return Array.Empty<byte>();
        }
    }

    public async Task<byte[]> GenerateInvestmentCertificatePdfAsync(
        string regBk,
        string regBr,
        string regNo,
        string finYear,
        CancellationToken cancellationToken = default)
    {
        if (!HasCompleteRegistration(regBk, regBr, regNo))
            return Array.Empty<byte>();

        try
        {
            var data = await LoadCertificateDataAsync(regBk, regBr, regNo, finYear, cancellationToken);
            if (data == null)
            {
                _logger.LogWarning("No investment certificate data found for {RegBk}/{RegBr}/{RegNo}", regBk, regBr, regNo);
                return Array.Empty<byte>();
            }

            if (data.NoOfCipUnit == null)
            {
                _logger.LogInformation("No CIP units for {RegBk}/{RegBr}/{RegNo} - investment certificate skipped", regBk, regBr, regNo);
                return Array.Empty<byte>();
            }

            var ruleText = await GetInvestmentRuleTextAsync(finYear, cancellationToken);

            return RenderCertificate(CertificatePdfBuilder.InvestmentType, data, ruleText, finYear, challans: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating investment PDF for {RegBk}/{RegBr}/{RegNo}", regBk, regBr, regNo);
            return Array.Empty<byte>();
        }
    }

    private bool HasCompleteRegistration(string regBk, string regBr, string regNo)
    {
        if (!string.IsNullOrWhiteSpace(regBk) && !string.IsNullOrWhiteSpace(regBr) && !string.IsNullOrWhiteSpace(regNo))
            return true;

        _logger.LogWarning("Registration details are incomplete: {RegBk}/{RegBr}/{RegNo}", regBk, regBr, regNo);
        return false;
    }

    private byte[] RenderCertificate(
        string type,
        HolderCertificateInfo data,
        string? ruleText,
        string finYear,
        IReadOnlyList<ChallanInfo>? challans)
    {
        var view = CertificatePdfBuilder.BuildViewModel(
            type,
            data,
            ruleText,
            certDate: DateTime.Now.ToString("dd-MMM-yyyy"),
            letterNo: ResolveLetterNo(type, finYear),
            challans);

        if (view == null)
        {
            _logger.LogError("Unknown certificate type {Type}", type);
            return Array.Empty<byte>();
        }

        var pdfBytes = CertificatePdfBuilder.BuildCertificatePdf(view);

        _logger.LogDebug("Generated {Type} certificate PDF with {ByteCount} bytes", type, pdfBytes.Length);

        return pdfBytes;
    }

    /// <summary>
    /// Memo number printed at the top left of a certificate, from
    /// configuration so that changing it needs no rebuild. A per-certificate
    /// setting overrides the shared one and a {FIN_YEAR} placeholder in the
    /// value is replaced with the selected financial year. Kept in step with
    /// the CertificateLetterNo keys in the UFMS Web.config.
    /// </summary>
    private string ResolveLetterNo(string type, string finYear)
    {
        var configured = type == CertificatePdfBuilder.InvestmentType
            ? _appSettings.CertificateLetterNoInvestment
            : _appSettings.CertificateLetterNoIncomeTax;

        if (string.IsNullOrWhiteSpace(configured))
            configured = _appSettings.CertificateLetterNo;

        if (string.IsNullOrWhiteSpace(configured))
            return "No-UF/TDS/" + finYear;

        return "No: " + configured.Trim().Replace("{FIN_YEAR}", finYear ?? "");
    }

    // ── Data access ───────────────────────────────────────────────────────────

    private async Task<HolderCertificateInfo?> LoadCertificateDataAsync(
        string regBk, string regBr, string regNo, string finYear, CancellationToken cancellationToken)
    {
        var key = $"{regBk}/{regBr}/{regNo}|{finYear}";
        if (_lastDataKey == key)
            return _lastData;

        var dt = await _repository.GetHolderCertificateDataAsync(regBk, regBr, regNo, finYear, cancellationToken);
        var data = dt == null || dt.Rows.Count == 0 ? null : MapCertificateData(dt.Rows[0]);

        _lastDataKey = key;
        _lastData = data;

        return data;
    }

    private static HolderCertificateInfo MapCertificateData(DataRow row) => new()
    {
        RegBk = GetString(row, "REG_BK"),
        RegBr = GetString(row, "REG_BR"),
        RegNo = GetString(row, "REG_NO"),
        Name1 = GetString(row, "NAME1"),
        Name2 = GetString(row, "NAME2"),
        Name3 = GetString(row, "NAME3"),
        Name4 = GetString(row, "NAME4"),
        ContactAddress1 = GetString(row, "CONTACT_ADDRSS1"),
        ContactAddress2 = GetString(row, "CONTACT_ADDRSS2"),
        ContactAddress3 = GetString(row, "CONTACT_ADDRSS3"),
        Etin = GetString(row, "ETIN1"),
        WarrantDate = GetString(row, "WARRENT_DATE"),
        Balance = GetDecimal(row, "BALANCE"),
        DividendRate = GetDecimal(row, "DIVIDEND_RATE"),
        GrossDividend = GetDecimal(row, "GROSS_DIVIDEND"),
        TaxLabel = GetString(row, "TAX_LABEL"),
        TaxDeduction = GetDecimal(row, "TAX_DEDUCTION"),
        NetDividend = GetDecimal(row, "NET_DIVIDENT"),
        FinYear = GetString(row, "FIN_YEAR"),
        YearEndDate = Convert.ToDateTime(row["YEAR_END_DATE"]),
        NoOfCipUnit = GetNullableDecimal(row, "NO_OF_CIP_UNIT"),
        CipRate = GetDecimal(row, "CIP_RATE"),
        Amount = GetDecimal(row, "AMOUNT")
    };

    private async Task<string> GetIncomeTaxRuleTextAsync(string finYear, CancellationToken cancellationToken)
    {
        if (_incomeTaxRuleCache.TryGetValue(finYear, out var cached))
            return cached;

        var dt = await _repository.GetIncomeTaxRuleNameAsync(finYear, cancellationToken);
        var text = dt?.Rows.Count > 0 ? GetString(dt.Rows[0], "INCOME_TAX_RULE_NAME") : "";

        _incomeTaxRuleCache[finYear] = text;
        return text;
    }

    private async Task<string> GetInvestmentRuleTextAsync(string finYear, CancellationToken cancellationToken)
    {
        if (_investmentRuleCache.TryGetValue(finYear, out var cached))
            return cached;

        var dt = await _repository.GetInvestmentRuleNameAsync(finYear, cancellationToken);
        var text = dt?.Rows.Count > 0 ? GetString(dt.Rows[0], "INVESTMENT_RULE_NAME") : "";

        _investmentRuleCache[finYear] = text;
        return text;
    }

    /// <summary>
    /// The challans of the tax payment table. A failure here is not fatal: the
    /// certificate is still worth issuing, it then simply ends at the rules.
    /// </summary>
    private async Task<List<ChallanInfo>> GetChallansAsync(string finYear, CancellationToken cancellationToken)
    {
        if (_challanCache.TryGetValue(finYear, out var cached))
            return cached;

        var challans = new List<ChallanInfo>();

        try
        {
            var dt = await _repository.GetChallanListAsync(finYear, cancellationToken);
            if (dt != null)
            {
                foreach (DataRow row in dt.Rows)
                {
                    challans.Add(new ChallanInfo
                    {
                        Serial = row["SERIAL"] == DBNull.Value ? 0 : Convert.ToInt32(row["SERIAL"]),
                        ChallanNo = GetString(row, "CHALLAN_NO"),
                        ChallanDate = row["CHALLAN_DATE"] == DBNull.Value ? null : Convert.ToDateTime(row["CHALLAN_DATE"]),
                        BankName = GetString(row, "BANK_NAME"),
                        BranchName = GetString(row, "BRANCH_NAME")
                    });
                }
            }

            if (challans.Count == 0)
                _logger.LogInformation("No approved challan found for {FinYear} - the tax payment table is left off the certificate", finYear);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the challan list for {FinYear} - the tax payment table is left off the certificate", finYear);
        }

        _challanCache[finYear] = challans;
        return challans;
    }

    private static string GetString(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value)
            return "";

        return row[column]?.ToString() ?? "";
    }

    private static decimal GetDecimal(DataRow row, string column)
    {
        return GetNullableDecimal(row, column) ?? 0m;
    }

    private static decimal? GetNullableDecimal(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value)
            return null;

        return Convert.ToDecimal(row[column]);
    }
}
