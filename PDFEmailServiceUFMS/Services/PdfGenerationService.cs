using System.Data;
using Microsoft.Extensions.Logging;
using PDFEmailServiceUFMS.Repositories.IRepository;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PDFEmailServiceUFMS.Services;

/// <summary>
/// Generates the Income Tax and Investment Certificate PDFs with QuestPDF:
/// scanned ICB letterhead on top, Unit Fund Department title, boxed
/// registration number, particulars table, numbered notes and the
/// signature block.
/// </summary>
public class PdfGenerationService : IPdfGenerationService
{
    private const string SignatoryName = "Md. Golam Mostofa";
    private const string SignatoryTitle = "Assistant General Manager";

    private static readonly Lazy<byte[]?> LetterheadImage = new(() => LoadAsset("letterhead.jpg"));
    private static readonly Lazy<byte[]?> SignatureImage = new(() => LoadAsset("signature.png"));

    private readonly IUnitFundRepository _repository;
    private readonly ILogger<PdfGenerationService> _logger;

    static PdfGenerationService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

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

            var model = BuildIncomeTaxModel(dividendData, taxRuleData);

            return RenderIncomeTaxPdf(model);
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

            var model = BuildInvestmentModel(dt, regBk, regBr, regNo);

            return RenderInvestmentPdf(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating investment PDF for {RegBk}/{RegBr}/{RegNo}", regBk, regBr, regNo);
            return Array.Empty<byte>();
        }
    }

    // ── Data models ───────────────────────────────────────────────────────────

    private sealed record TaxCertificateModel(
        string LetterNo,
        string InputDate,
        string RegistrationNo,
        string NameAddress,
        string YearEndDateStr,
        string WarrantDateStr,
        string Units,
        string DividendRate,
        string GrossDividend,
        string TaxHeader,
        string TaxDeduction,
        string NetDividend,
        string IncomeTaxRuleText);

    private sealed record InvestmentCertificateModel(
        string LetterNo,
        string InputDate,
        string RegistrationNo,
        string NameAddress,
        string YearEndDateStr,
        string DateOfIssue,
        string Units,
        string RatePerUnit,
        string Amount,
        string InvestmentYear);

    private static TaxCertificateModel BuildIncomeTaxModel(DataTable dividendData, DataTable? taxRuleData)
    {
        var row = dividendData.Rows[0];

        DateTime yearEnd = Convert.ToDateTime(row["YEAR_END_DATE"]);

        DateTime warrantDate;
        if (!DateTime.TryParseExact(
                row["WARRENT_DATE"].ToString(),
                "dd/MM/yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out warrantDate))
        {
            DateTime.TryParse(
                row["WARRENT_DATE"].ToString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out warrantDate);
        }

        return new TaxCertificateModel(
            LetterNo: "No.-018/",
            InputDate: DateTime.Now.ToString("dd-MMM-yyyy"),
            RegistrationNo: $"{row["REG_BK"]}/{row["REG_BR"]}/{row["REG_NO"]}",
            NameAddress: BuildNameAddress(row),
            YearEndDateStr: FormatMyDate(yearEnd, includeComma: false),
            WarrantDateStr: FormatMyDate(warrantDate, includeComma: true),
            Units: FormatNumber(row["BALANCE"]),
            DividendRate: FormatNumber(row["DIVIDEND_RATE"]),
            GrossDividend: FormatMoney(row["GROSS_DIVIDEND"]),
            TaxHeader: $"Tax Deduction {row["TAX"]}",
            TaxDeduction: FormatMoney(row["TAX_DEDUCTION"]),
            NetDividend: FormatMoney(row["NET_DIVIDENT"]),
            IncomeTaxRuleText: taxRuleData?.Rows.Count > 0
                ? taxRuleData.Rows[0]["INCOME_TAX_RULE_NAME"]?.ToString()?.Trim() ?? ""
                : "");
    }

    private static InvestmentCertificateModel BuildInvestmentModel(DataTable dt, string regBk, string regBr, string regNo)
    {
        var row = dt.Rows[0];

        DateTime yearEnd = Convert.ToDateTime(row["YEAR_END_DATE"]);

        // Investment credit applies to the NEXT financial year (e.g. 2025-2026 -> 2026-2027)
        string finYear = row["FIN_YEAR"].ToString() ?? "";
        string[] parts = finYear.Split('-');
        string invYear = parts.Length == 2
            ? $"{Convert.ToInt32(parts[0]) + 1}-{Convert.ToInt32(parts[1]) + 1}"
            : finYear;

        return new InvestmentCertificateModel(
            LetterNo: "No.-018/",
            InputDate: DateTime.Now.ToString("dd-MMM-yyyy"),
            RegistrationNo: $"{regBk}/{regBr}/{regNo}",
            NameAddress: BuildNameAddress(row),
            YearEndDateStr: FormatMyDate(yearEnd, includeComma: false),
            DateOfIssue: row["WARRENT_DATE"]?.ToString() ?? "",
            Units: FormatNumber(row["NO_OF_CIP_UNIT"]),
            RatePerUnit: FormatMoney(row["CIP_RATE"]),
            Amount: FormatMoney(row["AMOUNT"]),
            InvestmentYear: invYear);
    }

    private static string BuildNameAddress(DataRow row)
    {
        string[] names =
        {
            row.Table.Columns.Contains("NAME1") ? row["NAME1"]?.ToString() ?? "" : "",
            row.Table.Columns.Contains("NAME2") ? row["NAME2"]?.ToString() ?? "" : "",
            row.Table.Columns.Contains("NAME3") ? row["NAME3"]?.ToString() ?? "" : "",
            row.Table.Columns.Contains("NAME4") ? row["NAME4"]?.ToString() ?? "" : ""
        };
        string[] addresses =
        {
            row.Table.Columns.Contains("CONTACT_ADDRSS1") ? row["CONTACT_ADDRSS1"]?.ToString() ?? "" : "",
            row.Table.Columns.Contains("CONTACT_ADDRSS2") ? row["CONTACT_ADDRSS2"]?.ToString() ?? "" : "",
            row.Table.Columns.Contains("CONTACT_ADDRSS3") ? row["CONTACT_ADDRSS3"]?.ToString() ?? "" : ""
        };

        var lines = names.Where(n => !string.IsNullOrWhiteSpace(n))
            .Concat(new[] { "" }) // blank line between names and address, as in the original
            .Concat(addresses.Where(a => !string.IsNullOrWhiteSpace(a)))
            .Select(l => l.Trim());

        return string.Join("\n", lines).Trim();
    }

    /// <summary>1st/2nd/3rd/... formatted date, e.g. "30th June 2026" or "30th July, 2026".</summary>
    private static string FormatMyDate(DateTime date, bool includeComma)
    {
        int day = date.Day;
        string suffix = day switch
        {
            1 or 21 or 31 => "st",
            2 or 22 => "nd",
            3 or 23 => "rd",
            _ => "th"
        };

        return includeComma
            ? $"{day}{suffix} {date:MMMM}, {date.Year}"
            : $"{day}{suffix} {date:MMMM yyyy}";
    }

    private static string FormatMoney(object? value)
    {
        if (value == null || value == DBNull.Value) return "";
        return decimal.TryParse(value.ToString(), out var d) ? d.ToString("N2") : value.ToString() ?? "";
    }

    private static string FormatNumber(object? value)
    {
        if (value == null || value == DBNull.Value) return "";
        return decimal.TryParse(value.ToString(), out var d) ? d.ToString("0.##") : value.ToString() ?? "";
    }

    private static byte[]? LoadAsset(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    // ── PDF composition ───────────────────────────────────────────────────────

    private byte[] RenderIncomeTaxPdf(TaxCertificateModel m)
    {
        return RenderCertificate("income tax", column =>
        {
            ComposeLetterAndAddressee(column, m.LetterNo, m.InputDate, m.RegistrationNo, m.NameAddress);

            ComposeSubject(column, "Income Tax Certificate");

            column.Item().PaddingTop(12).Text(t =>
            {
                t.Justify();
                t.Span("This is to certify that Investment Corporation of Bangladesh issued dividend warrant " +
                       $"to the above unit holder for the financial year ended {m.YearEndDateStr} which was " +
                       $"declared on {m.WarrantDateStr}. The particulars were as follows:");
            });

            column.Item().PaddingTop(12).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                });

                table.Cell().Element(HeaderCell).Text("No. of Unit");
                table.Cell().Element(HeaderCell).Text("Rate of Dividend (Tk.)");
                table.Cell().Element(HeaderCell).Text("Gross Dividend (Tk.)");
                table.Cell().Element(HeaderCell).Text(m.TaxHeader);
                table.Cell().Element(HeaderCell).Text("Net Dividend (Tk.)");

                table.Cell().Element(ValueCell).Text(m.Units);
                table.Cell().Element(ValueCell).Text(m.DividendRate);
                table.Cell().Element(ValueCell).Text(m.GrossDividend);
                table.Cell().Element(ValueCell).Text(m.TaxDeduction);
                table.Cell().Element(ValueCell).Text(m.NetDividend);
            });

            // Note (2) comes verbatim from UNIT_PARAMETERS.INCOME_TAX_RULE_NAME
            if (!string.IsNullOrWhiteSpace(m.IncomeTaxRuleText))
            {
                column.Item().PaddingTop(14).Text(t =>
                {
                    t.Justify();
                    t.Span(m.IncomeTaxRuleText);
                });
            }

            column.Item().PaddingTop(10).Text(t =>
            {
                t.Justify();
                t.Span("(3) Income Tax deducted at source will be duly deposited to the Income Tax Authority, " +
                       "Government of the People's Republic of Bangladesh.");
            });

            ComposeSignatureBlock(column);
        });
    }

    private byte[] RenderInvestmentPdf(InvestmentCertificateModel m)
    {
        return RenderCertificate("investment", column =>
        {
            ComposeLetterAndAddressee(column, m.LetterNo, m.InputDate, m.RegistrationNo, m.NameAddress);

            ComposeSubject(column, "Investment Certificate");

            column.Item().PaddingTop(12).Text(t =>
            {
                t.Justify();
                t.Span("This is to certify that the Corporation has issued ICB Unit Certificates under CIP " +
                       $"against the net dividend income for the financial year ended {m.YearEndDateStr} to the " +
                       "above unit holder. The particulars are as follows:");
            });

            column.Item().PaddingTop(12).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                });

                table.Cell().Element(HeaderCell).Text("Date of Issue");
                table.Cell().Element(HeaderCell).Text("No. of Unit");
                table.Cell().Element(HeaderCell).Text("Rate per Unit (Tk.)");
                table.Cell().Element(HeaderCell).Text("Amount (Tk.)");

                table.Cell().Element(ValueCell).Text(m.DateOfIssue);
                table.Cell().Element(ValueCell).Text(m.Units);
                table.Cell().Element(ValueCell).Text(m.RatePerUnit);
                table.Cell().Element(ValueCell).Text(m.Amount);
            });

            column.Item().PaddingTop(14).Text(t =>
            {
                t.Justify();
                t.Span($"(2) The holder is entitled to investment credit for the Financial Year {m.InvestmentYear} " +
                       "under section 76 and section 78 of part-C, of the Sixth Schedule of the Income Tax Act-2023.");
            });

            ComposeSignatureBlock(column);
        });
    }

    /// <summary>Shared page frame: A4, letterhead image on top, then the certificate body.</summary>
    private byte[] RenderCertificate(string reportKind, Action<ColumnDescriptor> composeBody)
    {
        try
        {
            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginTop(28);
                    page.MarginBottom(40);
                    page.MarginHorizontal(42);
                    page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(11).FontColor(Colors.Black));

                    page.Content().Column(column =>
                    {
                        // Scanned ICB letterhead (logo + Bengali/English name + address)
                        if (LetterheadImage.Value is { } letterhead)
                        {
                            column.Item().Image(letterhead).FitWidth();
                        }
                        else
                        {
                            column.Item().AlignCenter().Text("INVESTMENT CORPORATION OF BANGLADESH")
                                .FontSize(16).Bold();
                            column.Item().AlignCenter().Text("8, DIT Avenue (Level 14-21), Dhaka, Bangladesh")
                                .FontSize(9);
                        }

                        column.Item().PaddingTop(22).AlignCenter()
                            .Text("Unit Fund Department").FontSize(14).Bold().Underline();

                        composeBody(column);
                    });
                });
            }).GeneratePdf();

            _logger.LogDebug("Generated {ReportKind} PDF with {ByteCount} bytes", reportKind, pdfBytes.Length);

            return pdfBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering {ReportKind} PDF report", reportKind);
            return Array.Empty<byte>();
        }
    }

    /// <summary>Letter No. + Date row, boxed Registration No., and the Mr./Mrs./Miss. addressee block.</summary>
    private static void ComposeLetterAndAddressee(ColumnDescriptor column, string letterNo, string inputDate, string registrationNo, string nameAddress)
    {
        column.Item().PaddingTop(16).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(letterNo);
                left.Item().PaddingTop(10).Row(r =>
                {
                    r.AutoItem().Text("Mr./Mrs./Miss.");
                    r.RelativeItem().PaddingLeft(16).Text(nameAddress).LineHeight(1.2f);
                });
            });

            row.ConstantItem(230).Column(right =>
            {
                right.Item().AlignRight().Text($"Date:  {inputDate}");
                right.Item().PaddingTop(8).Border(1).PaddingVertical(5).PaddingHorizontal(8)
                    .AlignCenter().Text($"Registration No.:  {registrationNo}").SemiBold();
            });
        });
    }

    private static void ComposeSubject(ColumnDescriptor column, string subject)
    {
        column.Item().PaddingTop(26).AlignCenter().Text(t =>
        {
            t.Span("Sub: ").FontSize(12).Bold();
            t.Span(subject).FontSize(12).Bold().Underline();
        });
    }

    /// <summary>"For and on behalf of ..." block with the signature image, right-aligned.</summary>
    private static void ComposeSignatureBlock(ColumnDescriptor column)
    {
        column.Item().PaddingTop(34).AlignRight().Width(250).Column(sig =>
        {
            sig.Item().AlignCenter().Text("For and on behalf of");
            sig.Item().AlignCenter().Text("Investment Corporation of Bangladesh");

            if (SignatureImage.Value is { } signature)
            {
                sig.Item().PaddingTop(8).AlignCenter().Width(95).Image(signature).FitWidth();
            }
            else
            {
                sig.Item().PaddingTop(36); // leave space for a manual signature
            }

            sig.Item().PaddingTop(6).AlignCenter().Text(SignatoryName);
            sig.Item().AlignCenter().Text(SignatoryTitle);
        });
    }

    private static IContainer HeaderCell(IContainer container) => container
        .Border(0.75f)
        .Background(Colors.Grey.Lighten4)
        .PaddingVertical(5)
        .PaddingHorizontal(4)
        .AlignCenter()
        .AlignMiddle()
        .DefaultTextStyle(s => s.SemiBold().FontSize(10.5f));

    private static IContainer ValueCell(IContainer container) => container
        .Border(0.75f)
        .PaddingVertical(5)
        .PaddingHorizontal(4)
        .AlignCenter()
        .AlignMiddle();
}
