using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PDFEmailServiceUFMS.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace PDFEmailServiceUFMS.Services;

/// <summary>
/// The Income Tax and Investment Certificate layout, ported from UFMS
/// (Models/BLL/HolderCertificatePdfBuilder.cs — the unitf059v1 screen) so that
/// the certificate a unit holder receives by email is the same document the
/// department prints from UFMS. BuildViewModel produces the display-ready
/// content, BuildCertificatePdf lays it out: ICB letterhead (logo + Bengali/
/// English titles and address, drawn as text), Unit Fund Department title,
/// boxed registration number, particulars table, numbered notes, the tax
/// payment table and the signature block.
/// </summary>
public static class CertificatePdfBuilder
{
    public const string IncomeTaxType = "IncomeTax";
    public const string InvestmentType = "Investment";

    private const string SignatoryName = "Md. Golam Mostofa";
    private const string SignatoryTitle = "Assistant General Manager";

    // Tax payment table on the Income Tax Certificate. The rows come from
    // UNIT_CHALLAN for the selected financial year; the page is laid out so
    // that the maximum still fits on one A4 sheet. The heading is numbered
    // after the rules of INCOME_TAX_RULE_NAME, so it is written here rather
    // than stored with them.
    private const int MaxChallanRows = 12;
    private const string TaxPaymentHeading = "Tax Payment Details:";

    // Letterhead text (matches the original scanned ICB letterhead)
    private const string BanglaTitle = "ইনভেস্টমেন্ট কর্পোরেশন অব বাংলাদেশ";
    private const string BanglaAddress = "৮, ডি আই টি এভিনিউ ( লেভেল ১৪-২১), ঢাকা, বাংলাদেশ, পিএবিএক্স : ৯৫৬৩৪৫৫ (অটো হান্টিং), ফ্যাক্স : ৮৮-০২-৯৫৬৩৩১৩";
    private const string EnglishAddress = "8, DIT AVENUE (Level 14-21), DHAKA, BANGLADESH, PABX : 9563455 (AUTO HUNTING), FAX : 88-02-9563313, E-mail : info@icb.gov.bd";

    // Bengali-capable font stack: Kalpurush (classic Bangla look), falling back
    // to Nirmala UI which ships with Windows 10/11
    private static readonly string[] BanglaFonts = { "Kalpurush", "Nirmala UI", "Shonar Bangla", "Vrinda" };

    private static readonly QuestPDF.Infrastructure.Color GoldColor = QuestPDF.Infrastructure.Color.FromHex("#A5872B");
    private static readonly QuestPDF.Infrastructure.Color InkColor = QuestPDF.Infrastructure.Color.FromHex("#1F1F1F");

    // Page geometry. The content height is needed on its own: it is the floor
    // the letter is measured against so that a short certificate is never
    // blown up by the scale-to-fit below (see BuildCertificatePdf).
    private const float PageMarginTop = 24;
    private const float PageMarginBottom = 26;
    private const float PageMarginHorizontal = 42;
    private static readonly float PageContentWidth =
        PageSizes.A4.Width - 2 * PageMarginHorizontal;
    private static readonly float PageContentHeight =
        PageSizes.A4.Height - PageMarginTop - PageMarginBottom;

    private static readonly Lazy<byte[]?> LogoImage = new(() => LoadAsset("ICBLogo.jpg"));
    private static readonly Lazy<byte[]?> SignatureImage = new(() => LoadAsset("signature.png"));

    // ── View model ────────────────────────────────────────────────────────────

    /// <summary>
    /// The display-ready certificate content for the given type
    /// (IncomeTax | Investment); null for an unknown type.
    /// <paramref name="ruleText"/> is the wording stored in UNIT_PARAMETERS for
    /// this financial year — INCOME_TAX_RULE_NAME for the Income Tax
    /// Certificate, INVESTMENT_RULE_NAME for the Investment Certificate.
    /// <paramref name="challans"/> is only used by the Income Tax Certificate
    /// and may be null.
    /// </summary>
    public static CertificateView? BuildViewModel(
        string type,
        HolderCertificateInfo data,
        string? ruleText,
        string certDate,
        string letterNo,
        IReadOnlyList<ChallanInfo>? challans)
    {
        var view = new CertificateView
        {
            Type = type,
            LetterNo = letterNo,
            CertDate = certDate,
            RegistrationNo = $"{data.RegBk}/{data.RegBr}/{data.RegNo}"
        };

        foreach (var name in new[] { data.Name1, data.Name2, data.Name3, data.Name4 })
        {
            if (!string.IsNullOrWhiteSpace(name))
                view.NameLines.Add(name.Trim());
        }

        foreach (var address in new[] { data.ContactAddress1, data.ContactAddress2, data.ContactAddress3 })
        {
            if (!string.IsNullOrWhiteSpace(address))
                view.AddressLines.Add(address.Trim());
        }

        // Both certificates carry the holder's e-TIN as the last line of the addressee block.
        if (!string.IsNullOrWhiteSpace(data.Etin))
            view.EtinLine = "e-TIN No- " + data.Etin.Trim();

        var yearEndStr = FormatMyDate(data.YearEndDate, includeComma: false);

        if (type == IncomeTaxType)
        {
            view.Title = "Income Tax Certificate";
            view.Subject = "Certificate of Tax Deduction at Source";

            view.BodyText =
                "This is to certify that Investment Corporation of Bangladesh has paid the dividend " +
                "to the above mentioned unit holder for the financial year ended " +
                FormatPlainDate(data.YearEndDate) + " (declared on " +
                FormatWarrantDate(data.WarrantDate) + "). The particulars are as follows:";

            view.TableHeaders.Add("No. of Unit");
            view.TableHeaders.Add("Rate of Dividend (Tk.)");
            view.TableHeaders.Add("Gross Dividend (Tk.)");
            view.TableHeaders.Add("Tax Deduction " + data.TaxLabel);
            view.TableHeaders.Add("Net Dividend (Tk.)");

            view.TableValues.Add(FormatNumber(data.Balance));
            view.TableValues.Add(FormatNumber(data.DividendRate));
            view.TableValues.Add(FormatMoney(data.GrossDividend));
            view.TableValues.Add(FormatMoney(data.TaxDeduction));
            view.TableValues.Add(FormatMoney(data.NetDividend));

            // The rules below the table — (2), (3) and any further one — are the
            // wording stored in UNIT_PARAMETERS.INCOME_TAX_RULE_NAME for this
            // financial year, so the certificate follows the Act in force that
            // year without a code change. The tax payment heading and table
            // follow them, numbered on from there.
            AddRuleNotes(view, ruleText);

            AddChallanTable(view, challans);

            return view;
        }

        if (type == InvestmentType)
        {
            view.Title = "Investment Certificate";
            view.Subject = view.Title;

            // Investment credit applies to the NEXT financial year (e.g. 2025-2026 -> 2026-2027)
            var invYear = data.FinYear;
            var parts = (data.FinYear ?? "").Split('-');
            if (parts.Length == 2)
                invYear = $"{Convert.ToInt32(parts[0]) + 1}-{Convert.ToInt32(parts[1]) + 1}";

            view.BodyText =
                "This is to certify that the Corporation has issued ICB Unit Certificates under CIP " +
                "against the net dividend income for the financial year ended " + yearEndStr + " to the " +
                "above unit holder. The particulars are as follows:";

            view.TableHeaders.Add("Date of Issue");
            view.TableHeaders.Add("No. of Unit");
            view.TableHeaders.Add("Rate per Unit (Tk.)");
            view.TableHeaders.Add("Amount (Tk.)");

            view.TableValues.Add(data.WarrantDate ?? "");
            view.TableValues.Add(FormatNumber(data.NoOfCipUnit ?? 0));
            view.TableValues.Add(FormatMoney(data.CipRate));
            view.TableValues.Add(FormatMoney(data.Amount));

            // The note comes from UNIT_PARAMETERS.INVESTMENT_RULE_NAME for this
            // financial year, with the year of the credit filled into its
            // placeholder. A year that has no wording stored yet keeps the
            // sentence the certificate has always carried.
            var investmentNote = string.IsNullOrWhiteSpace(ruleText)
                ? "(2) The holder is entitled to investment credit for the Financial Year {FIN_YEAR} " +
                  "under section 76 and section 78 of part-C, of the Sixth Schedule of the Income Tax Act-2023."
                : ruleText;

            AddRuleNotes(view, FillYearPlaceholder(investmentNote, invYear));

            return view;
        }

        return null;
    }

    // Leading "(2)" / "(12)" marker of a note.
    private static readonly Regex NoteMarker = new(@"^\((\d+)\)\s*", RegexOptions.Compiled);

    // Placeholder for the financial year inside a stored rule wording. The
    // braces are what marks the spot, so "{FIN_YEAR}" and a note that still
    // carries a sample year such as "{2026-2027}" both work.
    private static readonly Regex YearPlaceholder = new(@"\{[^{}]*\}", RegexOptions.Compiled);

    /// <summary>Puts <paramref name="year"/> where the rule wording marks it with braces.</summary>
    private static string FillYearPlaceholder(string? text, string? year)
    {
        return YearPlaceholder.Replace(text ?? "", year ?? "");
    }

    /// <summary>
    /// Adds one note, keeping its "(n)" marker in a field of its own so the
    /// renderer can indent the wrapped lines under the text rather than under
    /// the marker.
    /// </summary>
    private static void AddNote(CertificateView view, string? text)
    {
        var note = (text ?? "").Trim();
        if (note.Length == 0)
            return;

        var item = new CertificateNote();
        var match = NoteMarker.Match(note);
        if (match.Success)
        {
            item.Marker = "(" + match.Groups[1].Value + ")";
            item.Text = note[match.Length..];
        }
        else
        {
            item.Marker = "";
            item.Text = note;
        }

        view.Notes.Add(item);
    }

    /// <summary>
    /// Turns the stored rule wording into the notes below the particulars
    /// table. The column holds the numbered rules only, one note per line —
    /// "(2) ...", "(3) ...", and so on — and a line that does not open with a
    /// "(n)" marker is a wrapped continuation of the note above it. The tax
    /// payment heading is NOT part of that wording: it is numbered and added by
    /// AddChallanTable, and a stored copy left over from an earlier year is
    /// dropped so it cannot appear twice.
    /// </summary>
    private static void AddRuleNotes(CertificateView view, string? ruleText)
    {
        if (string.IsNullOrWhiteSpace(ruleText))
            return;

        var lines = ruleText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        foreach (var line in lines)
        {
            var text = line.Trim();
            if (text.Length == 0)
                continue;

            if (view.Notes.Count > 0 && !NoteMarker.IsMatch(text))
            {
                var previous = view.Notes[^1];
                previous.Text = (previous.Text + " " + text).Trim();
                continue;
            }

            if (IsTaxPaymentHeading(text))
                continue;

            AddNote(view, text);
        }
    }

    /// <summary>Is this rule line the tax payment heading the code adds itself?</summary>
    private static bool IsTaxPaymentHeading(string text)
    {
        var body = NoteMarker.Replace(text ?? "", "").Trim().TrimEnd(':').Trim();
        return body.Equals(TaxPaymentHeading.TrimEnd(':'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds the tax payment heading and table from the approved challans of the
    /// financial year, oldest first, capped at what one A4 sheet holds. Neither
    /// appears when the year has no challan: there is nothing to certify a
    /// payment with, so the certificate simply ends at the rules.
    /// </summary>
    private static void AddChallanTable(CertificateView view, IReadOnlyList<ChallanInfo>? challans)
    {
        if (challans == null || challans.Count == 0)
            return;

        foreach (var challan in challans.OrderBy(c => c.Serial).Take(MaxChallanRows))
        {
            view.ChallanRows.Add(new CertificateChallan
            {
                ChallanNo = (challan.ChallanNo ?? "").Trim(),
                ChallanDate = challan.ChallanDate.HasValue
                    ? challan.ChallanDate.Value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)
                    : "",
                BankBranch = JoinBankBranch(challan.BankName, challan.BranchName)
            });
        }

        // The heading continues the numbering of the stored rules: with "(2)"
        // and "(3)" in INCOME_TAX_RULE_NAME it becomes "(4)". The opening
        // paragraph of the letter is the unnumbered "(1)", which is why an
        // empty rule text still starts the count at two.
        AddNote(view, "(" + (LastNoteNumber(view) + 1) + ") " + TaxPaymentHeading);

        view.ChallanHeaders.Add("Challan Number");
        view.ChallanHeaders.Add("Challan Date");
        view.ChallanHeaders.Add("Bank & Branch Name");
    }

    /// <summary>Number of the last numbered note; 1 when there is none.</summary>
    private static int LastNoteNumber(CertificateView view)
    {
        for (int i = view.Notes.Count - 1; i >= 0; i--)
        {
            var match = NoteMarker.Match(view.Notes[i].Marker ?? "");
            if (match.Success)
                return int.Parse(match.Groups[1].Value);
        }

        return 1;
    }

    private static string JoinBankBranch(string? bankName, string? branchName)
    {
        var bank = (bankName ?? "").Trim();
        var branch = (branchName ?? "").Trim();

        if (bank.Length > 0 && branch.Length > 0)
            return bank + ", " + branch;

        return bank.Length > 0 ? bank : branch;
    }

    // ── PDF renderer ──────────────────────────────────────────────────────────

    /// <summary>Renders the certificate view model as the QuestPDF letter.</summary>
    public static byte[] BuildCertificatePdf(CertificateView view)
    {
        return CreateDocument(view).GeneratePdf();
    }

    /// <summary>
    /// The certificate as a QuestPDF document. Split out from
    /// <see cref="BuildCertificatePdf"/> so the same layout can also be
    /// rasterised (GenerateImages) when checking a layout change.
    /// </summary>
    internal static IDocument CreateDocument(CertificateView view)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(PageMarginTop);
                page.MarginBottom(PageMarginBottom);
                page.MarginHorizontal(PageMarginHorizontal);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(11).FontColor(Colors.Black));

                // A certificate is a one-page letter, so the whole body is
                // scaled down when it would not fit - which only happens on an
                // Income Tax Certificate carrying a long tax payment table.
                // Everything shrinks together, so the layout stays proportional
                // instead of spilling onto a second sheet. MinHeight makes the
                // letter as tall as the page even when it is short, which stops
                // the same mechanism from scaling a two-line certificate UP to
                // fill the sheet.
                page.Content().ScaleToFit().MinHeight(PageContentHeight).Column(column =>
                {
                    column.Item().Element(ComposeLetterhead);

                    column.Item().PaddingTop(18).AlignCenter()
                        .Text("Unit Fund Department").FontSize(14).Bold().Underline();

                    ComposeLetterAndAddressee(column, view);

                    ComposeSubject(column, view.Subject);

                    column.Item().PaddingTop(12).Element(c => ComposeJustifiedParagraph(c, view.BodyText, PageContentWidth));

                    column.Item().PaddingTop(12).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            for (int i = 0; i < view.TableHeaders.Count; i++)
                                c.RelativeColumn();
                        });

                        // AlignCenter inside the text centres each wrapped line (e.g. "(Tk.)")
                        foreach (var header in view.TableHeaders)
                            table.Cell().Element(HeaderCell).Text(t =>
                            {
                                t.AlignCenter();
                                t.Span(header);
                            });

                        foreach (var value in view.TableValues)
                            table.Cell().Element(ValueCell).Text(value);
                    });

                    ComposeNotes(column, view);

                    ComposeChallanTable(column, view);

                    ComposeSignatureBlock(column);
                });
            });
        });
    }

    /// <summary>
    /// The ICB letterhead, drawn as text so no scanned image is needed:
    /// round emblem on the left; Bengali title, gold English title and the
    /// Bengali/English address lines beside it — same layout as the original.
    /// </summary>
    private static void ComposeLetterhead(IContainer container)
    {
        container.Column(header =>
        {
            header.Item().Row(row =>
            {
                row.ConstantItem(2);

                if (LogoImage.Value is { } logo)
                    row.ConstantItem(82).AlignMiddle().Image(logo).FitWidth();
                else
                    row.ConstantItem(82);

                row.RelativeItem().PaddingLeft(12).Column(text =>
                {
                    text.Item().AlignCenter().Text(BanglaTitle)
                        .FontFamily(BanglaFonts).FontSize(20).Bold().FontColor(InkColor);

                    text.Item().PaddingTop(2).LineHorizontal(1f).LineColor(InkColor);

                    text.Item().PaddingTop(3).AlignCenter().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontFamily("Times New Roman").FontColor(GoldColor).Bold());
                        AppendSmallCaps(t, "Investment");
                        t.Span("  ");
                        AppendSmallCaps(t, "Corporation");
                        t.Span("  ");
                        t.Span("OF").FontSize(12f);
                        t.Span("  ");
                        AppendSmallCaps(t, "Bangladesh");
                    });

                    text.Item().PaddingTop(3).LineHorizontal(1f).LineColor(InkColor);

                    // Height + ScaleToFit force each address onto a single line, as in the original
                    text.Item().PaddingTop(4).Height(12).ScaleToFit().AlignCenter().Text(BanglaAddress)
                        .FontFamily(BanglaFonts).FontSize(8.2f).FontColor(InkColor);

                    text.Item().PaddingTop(1).Height(10).ScaleToFit().AlignCenter().Text(EnglishAddress)
                        .FontFamily("Arial Narrow", "Arial").FontSize(7.4f).FontColor(InkColor);
                });
            });

            // thin rule closing the letterhead, as in the original
            header.Item().PaddingTop(6).LineHorizontal(0.9f).LineColor(Colors.Grey.Darken2);
        });
    }

    /// <summary>Small-caps effect for the gold English title: bigger first letter, smaller rest.</summary>
    private static void AppendSmallCaps(TextDescriptor t, string word)
    {
        t.Span(word[..1].ToUpperInvariant()).FontSize(16.5f);
        t.Span(word[1..].ToUpperInvariant()).FontSize(12f);
    }

    /// <summary>Letter No. + Date row, boxed Registration No., and the Mr./Mrs./Miss. addressee block.</summary>
    private static void ComposeLetterAndAddressee(ColumnDescriptor column, CertificateView view)
    {
        // One flush-left stack, line after line with no gaps: the salutation,
        // the names, the address, and the holder's e-TIN closing the block.
        var sb = new StringBuilder();
        sb.AppendLine("Mr./Mrs./Miss.");
        foreach (var name in view.NameLines)
            sb.AppendLine(name);
        foreach (var address in view.AddressLines)
            sb.AppendLine(address);
        if (!string.IsNullOrWhiteSpace(view.EtinLine))
            sb.AppendLine(view.EtinLine);
        var nameAddress = sb.ToString().Trim();

        column.Item().PaddingTop(14).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(view.LetterNo);
                left.Item().PaddingTop(10).Text(nameAddress).LineHeight(1.2f);
            });

            // Narrow enough that a full address line still fits beside it
            row.ConstantItem(208).Column(right =>
            {
                right.Item().AlignRight().Text($"Date:  {view.CertDate}");
                right.Item().PaddingTop(8).Border(1).PaddingVertical(5).PaddingHorizontal(8)
                    .AlignCenter().Text($"Registration No.:  {view.RegistrationNo}").SemiBold();
            });
        });
    }

    /// <summary>
    /// A paragraph justified so both edges line up (the body text and the
    /// numbered notes). Ported from UFMS unchanged: there QuestPDF 2022.12 has
    /// no justification of its own, so the words are measured with Skia,
    /// broken into lines against the given width, and every full line is laid
    /// out as a row of words with equal stretching gaps. QuestPDF 2025 could
    /// justify by itself, but it breaks lines a little later than that
    /// measurement does, and the emailed certificate must wrap exactly where
    /// the one printed from UFMS wraps - hence the same routine, measuring
    /// with the same SkiaSharp version. The last line stays flush left, as
    /// in any justified paragraph.
    /// </summary>
    private static void ComposeJustifiedParagraph(IContainer container, string text, float availableWidth)
    {
        const float fontSize = 11f;
        const float lineHeight = 1.25f;

        var lines = new List<List<string>>();
        using (var paint = new SKPaint())
        {
            paint.Typeface = SKTypeface.FromFamilyName("Arial");
            paint.TextSize = fontSize;
            float spaceWidth = paint.MeasureText(" ");

            var current = new List<string>();
            float currentWidth = 0;
            foreach (var word in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                float wordWidth = paint.MeasureText(word);
                float widthIfAdded = current.Count == 0 ? wordWidth : currentWidth + spaceWidth + wordWidth;
                // 1pt of slack keeps rounding from ever overfilling a row
                if (current.Count > 0 && widthIfAdded > availableWidth - 1)
                {
                    lines.Add(current);
                    current = new List<string>();
                    widthIfAdded = wordWidth;
                }
                current.Add(word);
                currentWidth = widthIfAdded;
            }
            if (current.Count > 0)
                lines.Add(current);
        }

        container.Column(paragraph =>
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var words = lines[i];
                bool isLast = i == lines.Count - 1;

                if (isLast || words.Count == 1)
                {
                    paragraph.Item().Text(string.Join(" ", words)).LineHeight(lineHeight);
                    continue;
                }

                paragraph.Item().Row(row =>
                {
                    for (int w = 0; w < words.Count; w++)
                    {
                        row.AutoItem().Text(words[w]).LineHeight(lineHeight);
                        if (w < words.Count - 1)
                            row.RelativeItem();
                    }
                });
            }
        });
    }

    private static void ComposeSubject(ColumnDescriptor column, string subject)
    {
        column.Item().PaddingTop(22).AlignCenter().Text(t =>
        {
            t.Span("Sub: ").FontSize(12).Bold();
            t.Span(subject).FontSize(12).Bold().Underline();
        });
    }

    /// <summary>The numbered notes, each with its "(n)" marker in a column of its own.</summary>
    private static void ComposeNotes(ColumnDescriptor column, CertificateView view)
    {
        const float markerWidth = 26;

        for (int i = 0; i < view.Notes.Count; i++)
        {
            var note = view.Notes[i];
            bool hasMarker = !string.IsNullOrEmpty(note.Marker);

            column.Item().PaddingTop(i == 0 ? 12 : 8).Row(row =>
            {
                if (hasMarker)
                    row.ConstantItem(markerWidth).Text(note.Marker);

                // justified like the body paragraph, within the width left beside the marker
                float textWidth = PageContentWidth - (hasMarker ? markerWidth : 0);
                row.RelativeItem().Element(c => ComposeJustifiedParagraph(c, note.Text, textWidth));
            });
        }
    }

    /// <summary>
    /// Tax payment table above the signature (Income Tax Certificate only).
    /// Row heights are kept tight so the twelve-row maximum still leaves room
    /// for the signature block on the same A4 sheet.
    /// </summary>
    private static void ComposeChallanTable(ColumnDescriptor column, CertificateView view)
    {
        if (view.ChallanHeaders.Count == 0)
            return;

        column.Item().PaddingTop(8).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);
                c.RelativeColumn(2);
                c.RelativeColumn(4);
            });

            foreach (var header in view.ChallanHeaders)
                table.Cell().Element(ChallanHeaderCell).Text(header);

            foreach (var row in view.ChallanRows)
            {
                table.Cell().Element(ChallanCell).Text(row.ChallanNo);
                table.Cell().Element(ChallanCell).Text(row.ChallanDate);
                table.Cell().Element(ChallanCell).Text(row.BankBranch);
            }
        });
    }

    /// <summary>"For and on behalf of ..." block with the signature image, right-aligned.</summary>
    private static void ComposeSignatureBlock(ColumnDescriptor column)
    {
        column.Item().PaddingTop(24).AlignRight().Width(250).Column(sig =>
        {
            sig.Item().AlignCenter().Text("For and on behalf of");
            sig.Item().AlignCenter().Text("Investment Corporation of Bangladesh");

            if (SignatureImage.Value is { } signature)
            {
                // signature.png is cropped tight to the ink, so the width
                // controls the whole footprint of the signature
                sig.Item().PaddingTop(2).AlignCenter().Width(70).Image(signature).FitWidth();
            }
            else
            {
                sig.Item().PaddingTop(28); // leave space for a manual signature
            }

            sig.Item().PaddingTop(2).AlignCenter().Text(SignatoryName);
            sig.Item().AlignCenter().Text(SignatoryTitle);
        });
    }

    private static IContainer ChallanHeaderCell(IContainer container) => container
        .Border(0.75f)
        .PaddingVertical(2)
        .PaddingHorizontal(4)
        .MinHeight(16)
        .AlignCenter()
        .AlignMiddle()
        .DefaultTextStyle(s => s.SemiBold().FontSize(10f));

    private static IContainer ChallanCell(IContainer container) => container
        .Border(0.75f)
        .PaddingVertical(1)
        .PaddingHorizontal(4)
        .MinHeight(14)
        .AlignCenter()
        .AlignMiddle()
        .DefaultTextStyle(s => s.FontSize(10f));

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

    // ── Formatting helpers ────────────────────────────────────────────────────

    /// <summary>1st/2nd/3rd/... formatted date, e.g. "30th June 2026" or "30th July, 2026".</summary>
    private static string FormatMyDate(DateTime date, bool includeComma)
    {
        int day = date.Day;
        var suffix = day switch
        {
            1 or 21 or 31 => "st",
            2 or 22 => "nd",
            3 or 23 => "rd",
            _ => "th"
        };

        return includeComma
            ? $"{day}{suffix} {date.ToString("MMMM", CultureInfo.InvariantCulture)}, {date.Year}"
            : $"{day}{suffix} {date.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}";
    }

    /// <summary>Plain date, e.g. "30 June 2026".</summary>
    private static string FormatPlainDate(DateTime date)
    {
        return $"{date.Day} {date.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// The dd/MM/yyyy warrant date string as "29 July 2026". Falls back to the
    /// raw text when the date cannot be parsed.
    /// </summary>
    private static string FormatWarrantDate(string? warrantDate)
    {
        if (DateTime.TryParseExact(warrantDate, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || DateTime.TryParse(warrantDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            return FormatPlainDate(parsed);
        }

        return warrantDate ?? "";
    }

    private static string FormatMoney(decimal value)
    {
        return value.ToString("N2", CultureInfo.InvariantCulture);
    }

    private static string FormatNumber(decimal value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static byte[]? LoadAsset(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
