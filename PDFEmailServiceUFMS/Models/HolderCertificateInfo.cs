namespace PDFEmailServiceUFMS.Models;

/// <summary>
/// Registration-wise data behind the holder certificates, as read from
/// UNIT_KYC + V_UNIT_DIVIDEND_ALL for one financial year. Mirrors
/// HOLDER_CERTIFICATE_INFO in UFMS (unitf059v1) so both applications feed the
/// certificate builder with the same fields.
/// </summary>
public sealed class HolderCertificateInfo
{
    public string RegBk { get; set; } = "";
    public string RegBr { get; set; } = "";
    public string RegNo { get; set; } = "";

    public string? Name1 { get; set; }
    public string? Name2 { get; set; }
    public string? Name3 { get; set; }
    public string? Name4 { get; set; }

    public string? ContactAddress1 { get; set; }
    public string? ContactAddress2 { get; set; }
    public string? ContactAddress3 { get; set; }

    public string? Etin { get; set; }

    /// <summary>Dividend declaration date as the raw "dd/MM/yyyy" text from the view.</summary>
    public string? WarrantDate { get; set; }

    public decimal Balance { get; set; }
    public decimal DividendRate { get; set; }
    public decimal GrossDividend { get; set; }

    /// <summary>"@Tk. &lt;rate&gt;%" suffix of the "Tax Deduction" column header.</summary>
    public string? TaxLabel { get; set; }

    public decimal TaxDeduction { get; set; }
    public decimal NetDividend { get; set; }

    public string FinYear { get; set; } = "";
    public DateTime YearEndDate { get; set; }

    /// <summary>Null when the holder did not reinvest the dividend as CIP units.</summary>
    public decimal? NoOfCipUnit { get; set; }

    public decimal CipRate { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>
/// One approved tax challan of a financial year (UNIT_CHALLAN), used by the
/// tax payment table on the Income Tax Certificate.
/// </summary>
public sealed class ChallanInfo
{
    public int Serial { get; set; }
    public string? ChallanNo { get; set; }
    public DateTime? ChallanDate { get; set; }
    public string? BankName { get; set; }
    public string? BranchName { get; set; }
}
