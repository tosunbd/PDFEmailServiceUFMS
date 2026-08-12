using System.Data;

namespace PDFEmailServiceUFMS.Repositories.IRepository;

public interface IUnitFundRepository
{
    /// <summary>
    /// Gets the current financial year from UNIT_DIVIDEND table.
    /// </summary>
    Task<DataTable?> GetFinancialYearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all distinct financial years from UNIT_DIVIDEND in descending order
    /// (used to populate the FIN_YEAR dropdown on the UI).
    /// </summary>
    Task<DataTable?> GetFinancialYearsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the income tax rule name for a given financial year.
    /// </summary>
    Task<DataTable?> GetIncomeTaxRuleNameAsync(string finYear, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all accounts with valid emails that have dividend data for the financial year.
    /// Returns: REG_BK, REG_BR, REG_NO, EMAIL, CIP_FLAG ('Y' when the holder reinvested
    /// dividends as CIP units and therefore also gets an Investment Certificate).
    /// </summary>
    Task<DataTable?> GetAccountEmailAsync(string finYear, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single account (with email and CIP_FLAG) for a specific registration
    /// (REG_BK / REG_BR / REG_NO) that has dividend data for the financial year.
    /// Used by the "specific registration" send mode. Returns null when not found.
    /// </summary>
    Task<DataTable?> GetAccountEmailByRegistrationAsync(string regBk, string regBr, string regNo, string finYear, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets dividend data for income tax certificate generation.
    /// </summary>
    Task<DataTable?> GetDividendDataAsync(string regBk, string regBr, string regNo, string finYear, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets investment certificate data for PDF generation.
    /// Returns: REG_BK, REG_BR, REG_NO, NAME1-4, CONTACT_ADDRSS1-3, WARRENT_DATE, WARRENT_NUMBER,
    /// NO_OF_CIP_UNIT, CIP_RATE, AMOUNT, FIN_YEAR, YEAR_END_DATE
    /// </summary>
    Task<DataTable?> GetInvestmentCertificateDataAsync(string regBk, string regBr, string regNo, string finYear, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets dividend receivable data for an account.
    /// </summary>
    Task<DataTable?> GetDividendReceivableAsync(int brCd, int accntNo, CancellationToken cancellationToken = default);
}
