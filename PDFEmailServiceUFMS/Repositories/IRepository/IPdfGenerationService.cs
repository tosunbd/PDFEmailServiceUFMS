namespace PDFEmailServiceUFMS.Repositories.IRepository;

public interface IPdfGenerationService
{
    /// <summary>
    /// Generates the Income Tax Certificate PDF (rptRSunitr004v1.rdlc).
    /// Returns an empty array when no data is found or rendering fails.
    /// </summary>
    Task<byte[]> GenerateIncomeTaxPdfAsync(
        string regBk,
        string regBr,
        string regNo,
        string finYear,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates the Investment Certificate PDF (rptRSunitr003v1.rdlc).
    /// Returns an empty array when no data is found or rendering fails.
    /// </summary>
    Task<byte[]> GenerateInvestmentCertificatePdfAsync(
        string regBk,
        string regBr,
        string regNo,
        string finYear,
        CancellationToken cancellationToken = default);
}
