namespace PDFEmailServiceUFMS.Models;

/// <summary>
/// Display-ready content of one holder certificate. Mirrors
/// HOLDER_CERTIFICATE_VIEW in UFMS (unitf059v1): the content is built once and
/// the renderer only lays it out, so the emailed certificate and the one the
/// operator prints from UFMS carry identical text.
/// </summary>
public sealed class CertificateView
{
    /// <summary>IncomeTax | Investment.</summary>
    public string Type { get; set; } = "";

    /// <summary>File-name heading, e.g. "Income Tax Certificate".</summary>
    public string Title { get; set; } = "";

    /// <summary>The "Sub:" line, e.g. "Certificate of Tax Deduction at Source".</summary>
    public string Subject { get; set; } = "";

    public string LetterNo { get; set; } = "";
    public string CertDate { get; set; } = "";
    public string RegistrationNo { get; set; } = "";

    public List<string> NameLines { get; } = new();
    public List<string> AddressLines { get; } = new();

    /// <summary>"e-TIN No- ..." last line of the addressee block; empty = not shown.</summary>
    public string EtinLine { get; set; } = "";

    public string BodyText { get; set; } = "";

    public List<string> TableHeaders { get; } = new();
    public List<string> TableValues { get; } = new();

    public List<CertificateNote> Notes { get; } = new();

    /// <summary>
    /// Tax payment (challan) table above the signature, Income Tax Certificate
    /// only. An empty header list means the certificate carries no such table.
    /// </summary>
    public List<string> ChallanHeaders { get; } = new();
    public List<CertificateChallan> ChallanRows { get; } = new();
}

/// <summary>
/// One numbered note. The marker is kept apart from the text so the renderer
/// can hang the wrapped lines under the first word instead of under the "(2)".
/// </summary>
public sealed class CertificateNote
{
    /// <summary>"(2)", "(3)", ...; empty for an unnumbered paragraph.</summary>
    public string Marker { get; set; } = "";

    public string Text { get; set; } = "";
}

/// <summary>One row of the tax payment table (UNIT_CHALLAN).</summary>
public sealed class CertificateChallan
{
    public string ChallanNo { get; set; } = "";
    public string ChallanDate { get; set; } = "";
    public string BankBranch { get; set; } = "";
}
