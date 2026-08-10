using System.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Reporting.NETCore;
using PDFEmailServiceUFMS.Repositories.IRepository;

namespace PDFEmailServiceUFMS.Services;

public class PdfGenerationService : IPdfGenerationService
{
    private readonly IUnitFundRepository _repository;
    private readonly ILogger<PdfGenerationService> _logger;

    public PdfGenerationService(IUnitFundRepository repository, ILogger<PdfGenerationService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<byte[]> GenerateIncomeTaxPdfAsync(
        string regBk,
        string regBr,
        string regNo,
        string finYear,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(regBk) || string.IsNullOrWhiteSpace(regBr) || string.IsNullOrWhiteSpace(regNo))
        {
            _logger.LogWarning("Registration details are incomplete: {RegBk}/{RegBr}/{RegNo}", regBk, regBr, regNo);
            return Array.Empty<byte>();
        }

        try
        {
            var dividendData = await _repository.GetDividendDataAsync(regBk, regBr, regNo, finYear, cancellationToken);
            if (dividendData == null || dividendData.Rows.Count == 0)
            {
                _logger.LogWarning("No dividend data found for {RegBk}/{RegBr}/{RegNo}", regBk, regBr, regNo);
                return Array.Empty<byte>();
            }

            var taxRuleData = await _repository.GetIncomeTaxRuleNameAsync(finYear, cancellationToken);

            var taxData = BuildIncomeTaxDataTable(dividendData, taxRuleData);

            return RenderPdf(taxData, "rptRSunitr004v1.rdlc", "rptDSunitr004v1", "income tax");
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
        if (string.IsNullOrWhiteSpace(regBk) || string.IsNullOrWhiteSpace(regBr) || string.IsNullOrWhiteSpace(regNo))
        {
            _logger.LogWarning("Registration details are incomplete: {RegBk}/{RegBr}/{RegNo}", regBk, regBr, regNo);
            return Array.Empty<byte>();
        }

        try
        {
            var dt = await _repository.GetInvestmentCertificateDataAsync(regBk, regBr, regNo, finYear, cancellationToken);

            if (dt == null || dt.Rows.Count == 0)
            {
                _logger.LogWarning("No investment certificate data found for {RegBk}/{RegBr}/{RegNo}", regBk, regBr, regNo);
                return Array.Empty<byte>();
            }

            if (dt.Rows[0]["NO_OF_CIP_UNIT"] == DBNull.Value)
            {
                _logger.LogInformation("No CIP units for {RegBk}/{RegBr}/{RegNo} - investment certificate skipped", regBk, regBr, regNo);
                return Array.Empty<byte>();
            }

            var investData = BuildInvestmentDataTable(dt, regBk, regBr, regNo);

            return RenderPdf(investData, "rptRSunitr003v1.rdlc", "rptDSunitr003v1", "investment");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating investment PDF for {RegBk}/{RegBr}/{RegNo}", regBk, regBr, regNo);
            return Array.Empty<byte>();
        }
    }

    private DataTable BuildIncomeTaxDataTable(DataTable dividendData, DataTable? taxRuleData)
    {
        var row = dividendData.Rows[0];

        string date = DateTime.Now.ToString("dd-MMM-yyyy");
        string lno = "No.-018/";

        DateTime dtmy = Convert.ToDateTime(row["YEAR_END_DATE"]);
        string yearall = $"{dtmy.Day}th {dtmy:MMMM yyyy}";

        string registrationFull = $"{row["REG_BK"]}/{row["REG_BR"]}/{row["REG_NO"]}";

        string[] names = {
            row["NAME1"]?.ToString() ?? "",
            row["NAME2"]?.ToString() ?? "",
            row["NAME3"]?.ToString() ?? "",
            row["NAME4"]?.ToString() ?? ""
        };
        string[] addresses = {
            row["CONTACT_ADDRSS1"]?.ToString() ?? "",
            row["CONTACT_ADDRSS2"]?.ToString() ?? "",
            row["CONTACT_ADDRSS3"]?.ToString() ?? ""
        };

        string fullNameAddress = string.Join(Environment.NewLine, names.Where(n => !string.IsNullOrWhiteSpace(n)))
            + Environment.NewLine
            + string.Join(" ", addresses.Where(a => !string.IsNullOrWhiteSpace(a)));

        DataTable taxData = new DataTable();
        taxData.Columns.Add("LETTER_NO");
        taxData.Columns.Add("WARRENT_DATE");
        taxData.Columns.Add("WARRENT_DATE_STR");
        taxData.Columns.Add("BALANCE");
        taxData.Columns.Add("DIVIDEND_RATE");
        taxData.Columns.Add("GROSS_DIVIDEND");
        taxData.Columns.Add("TAX");
        taxData.Columns.Add("TAX_DEDUCTION");
        taxData.Columns.Add("NET_DIVIDENT");
        taxData.Columns.Add("REGISTRATION_NO_FULL");
        taxData.Columns.Add("INPUT_DATE");
        taxData.Columns.Add("FULL_NAME_ADDRESS");
        taxData.Columns.Add("YEAR_END_DATE");
        taxData.Columns.Add("YEAR_END_DATE_STR");
        taxData.Columns.Add("INCOME_TAX_RULE_NAME");

        DataRow newRow = taxData.NewRow();
        newRow["LETTER_NO"] = lno;

        DateTime warrentDate;
        if (!DateTime.TryParseExact(
                row["WARRENT_DATE"].ToString(),
                "dd/MM/yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out warrentDate))
        {
            DateTime.TryParse(
                row["WARRENT_DATE"].ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out warrentDate);
        }

        newRow["WARRENT_DATE"] = warrentDate;
        newRow["WARRENT_DATE_STR"] = FormatMyDate(warrentDate);
        newRow["BALANCE"] = row["BALANCE"];
        newRow["DIVIDEND_RATE"] = row["DIVIDEND_RATE"];
        newRow["GROSS_DIVIDEND"] = row["GROSS_DIVIDEND"];
        newRow["TAX"] = row["TAX"];
        newRow["TAX_DEDUCTION"] = row["TAX_DEDUCTION"];
        newRow["NET_DIVIDENT"] = row["NET_DIVIDENT"];
        newRow["REGISTRATION_NO_FULL"] = registrationFull;
        newRow["INPUT_DATE"] = date;
        newRow["FULL_NAME_ADDRESS"] = fullNameAddress;
        newRow["YEAR_END_DATE"] = dtmy.ToString("dd-MMM-yyyy");
        newRow["YEAR_END_DATE_STR"] = yearall;
        newRow["INCOME_TAX_RULE_NAME"] = taxRuleData?.Rows.Count > 0
            ? taxRuleData.Rows[0]["INCOME_TAX_RULE_NAME"]?.ToString()?.Trim() ?? ""
            : "";

        taxData.Rows.Add(newRow);

        return taxData;
    }

    private DataTable BuildInvestmentDataTable(DataTable dt, string regBk, string regBr, string regNo)
    {
        string lno = "No.-018/";
        DateTime dtmy = Convert.ToDateTime(dt.Rows[0]["YEAR_END_DATE"]);
        string yearall = dtmy.Day + "th " + dtmy.ToString("MMMM yyyy");

        string strta = dt.Rows[0]["FIN_YEAR"].ToString() ?? "";
        string[] parts = strta.Split('-');

        string invYear1 = Convert.ToString(Convert.ToInt32(parts[0]) + 1);
        string invYear2 = Convert.ToString(Convert.ToInt32(parts[1]) + 1);
        string invYear = invYear1 + "-" + invYear2;

        // Build full name and address
        string fullNameAddress = dt.Rows[0]["NAME1"]?.ToString() + " " + Environment.NewLine;

        if (!string.IsNullOrEmpty(dt.Rows[0]["NAME2"]?.ToString()))
            fullNameAddress += dt.Rows[0]["NAME2"] + " " + Environment.NewLine;

        if (!string.IsNullOrEmpty(dt.Rows[0]["NAME3"]?.ToString()))
            fullNameAddress += dt.Rows[0]["NAME3"] + " " + Environment.NewLine;

        if (!string.IsNullOrEmpty(dt.Rows[0]["NAME4"]?.ToString()))
            fullNameAddress += dt.Rows[0]["NAME4"] + " " + Environment.NewLine;

        fullNameAddress = fullNameAddress + Environment.NewLine
            + dt.Rows[0]["CONTACT_ADDRSS1"] + " "
            + Environment.NewLine + dt.Rows[0]["CONTACT_ADDRSS2"] + " "
            + Environment.NewLine + dt.Rows[0]["CONTACT_ADDRSS3"];

        var investData = new DataTable();
        investData.Columns.Add("LETTER_NO", typeof(string));
        investData.Columns.Add("WARRENT_DATE", typeof(string));
        investData.Columns.Add("WARRENT_NUMBER", typeof(string));
        investData.Columns.Add("NO_OF_CIP_UNIT", typeof(string));
        investData.Columns.Add("CIP_RATE", typeof(string));
        investData.Columns.Add("AMOUNT", typeof(string));
        investData.Columns.Add("REGISTRATION_NO_FULL", typeof(string));
        investData.Columns.Add("INPUT_DATE", typeof(string));
        investData.Columns.Add("FULL_NAME_ADDRESS", typeof(string));
        investData.Columns.Add("FIN_YEAR", typeof(string));
        investData.Columns.Add("YEAR_END_DATE", typeof(string));
        investData.Columns.Add("INVESTMENT_YEAR", typeof(string));
        investData.Columns.Add("YEAR_END_DATE_STR", typeof(string));

        var newRow = investData.NewRow();
        newRow["LETTER_NO"] = lno;
        newRow["WARRENT_DATE"] = dt.Rows[0]["WARRENT_DATE"]?.ToString();
        newRow["WARRENT_NUMBER"] = dt.Rows[0]["WARRENT_NUMBER"]?.ToString();
        newRow["NO_OF_CIP_UNIT"] = dt.Rows[0]["NO_OF_CIP_UNIT"]?.ToString();
        newRow["CIP_RATE"] = dt.Rows[0]["CIP_RATE"]?.ToString();
        newRow["AMOUNT"] = dt.Rows[0]["AMOUNT"]?.ToString();
        newRow["REGISTRATION_NO_FULL"] = regBk + "/" + regBr + "/" + regNo;
        newRow["INPUT_DATE"] = DateTime.Now.ToString("dd-MMM-yyyy");
        newRow["FULL_NAME_ADDRESS"] = fullNameAddress;
        newRow["FIN_YEAR"] = dt.Rows[0]["FIN_YEAR"]?.ToString();
        newRow["YEAR_END_DATE"] = dt.Rows[0]["YEAR_END_DATE"]?.ToString();
        newRow["INVESTMENT_YEAR"] = invYear;
        newRow["YEAR_END_DATE_STR"] = yearall;

        investData.Rows.Add(newRow);

        return investData;
    }

    private static string FormatMyDate(DateTime warDt)
    {
        int day = warDt.Day;

        string suffix = "th";
        if (day == 1 || day == 21 || day == 31)
            suffix = "st";
        else if (day == 2 || day == 22)
            suffix = "nd";
        else if (day == 3 || day == 23)
            suffix = "rd";

        return $"{day}{suffix} {warDt:MMMM}, {warDt.Year}";
    }

    private byte[] RenderPdf(DataTable data, string reportFileName, string dataSourceName, string reportKind)
    {
        try
        {
            var reportPath = Path.Combine(AppContext.BaseDirectory, "RDLC", reportFileName);

            using var reportStream = new FileStream(reportPath, FileMode.Open, FileAccess.Read);
            var localReport = new LocalReport();
            localReport.LoadReportDefinition(reportStream);
            localReport.DataSources.Add(new ReportDataSource(dataSourceName, data));

            var pdfBytes = localReport.Render("PDF");

            _logger.LogDebug("Generated {ReportKind} PDF with {ByteCount} bytes", reportKind, pdfBytes.Length);

            return pdfBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering {ReportKind} PDF report", reportKind);
            return Array.Empty<byte>();
        }
    }
}
