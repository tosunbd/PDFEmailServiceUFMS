using System.Data;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using PDFEmailServiceUFMS.Repositories.IRepository;

namespace PDFEmailServiceUFMS.Repositories;

public class UnitFundRepository : IUnitFundRepository
{
    private readonly IOracleConnectionFactory _connectionFactory;
    private readonly ILogger<UnitFundRepository> _logger;

    public UnitFundRepository(IOracleConnectionFactory connectionFactory, ILogger<UnitFundRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<DataTable?> GetFinancialYearAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT MAX(FIN_YEAR) FIN_YEAR FROM UNIT_DIVIDEND";
        return await ExecuteQueryAsync(sql, Array.Empty<OracleParameter>(), cancellationToken);
    }

    public async Task<DataTable?> GetFinancialYearsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT DISTINCT FIN_YEAR FROM UNIT_DIVIDEND WHERE FIN_YEAR IS NOT NULL ORDER BY FIN_YEAR DESC";
        return await ExecuteQueryAsync(sql, Array.Empty<OracleParameter>(), cancellationToken);
    }

    public async Task<DataTable?> GetIncomeTaxRuleNameAsync(string finYear, CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT INCOME_TAX_RULE_NAME FROM UNIT_PARAMETERS WHERE FIN_YEAR = :V_FIN_YEAR";
        var parameters = new[] { new OracleParameter("V_FIN_YEAR", finYear) };
        return await ExecuteQueryAsync(sql, parameters, cancellationToken);
    }

    public async Task<DataTable?> GetInvestmentRuleNameAsync(string finYear, CancellationToken cancellationToken = default)
    {
        const string sql = @"SELECT INVESTMENT_RULE_NAME FROM UNIT_PARAMETERS WHERE FIN_YEAR = :V_FIN_YEAR";
        var parameters = new[] { new OracleParameter("V_FIN_YEAR", finYear) };
        return await ExecuteQueryAsync(sql, parameters, cancellationToken);
    }

    public async Task<DataTable?> GetChallanListAsync(string finYear, CancellationToken cancellationToken = default)
    {
        // Approved challans of the year, oldest first - the tax payment table on
        // the Income Tax Certificate. Same rows the UFMS checker screen shows.
        const string sql = @"
            SELECT
                C.SERIAL, C.CHALLAN_NO, C.CHALLAN_DATE, B.BANK_NAME, BR.BRANCH_NAME
            FROM UNIT_CHALLAN C
            LEFT JOIN UNIT_BANK_INFO B
                ON B.BANK_ID = C.BANK_ID
            LEFT JOIN UNIT_BRANCH_INFO BR
                ON BR.BANK_ID = C.BANK_ID
               AND TO_CHAR(BR.BRANCH_ID) = TRIM(C.BRANCH_ID)
            WHERE TRIM(C.FIN_YEAR) = TRIM(:V_FIN_YEAR)
            ORDER BY C.SERIAL";

        var parameters = new[] { new OracleParameter("V_FIN_YEAR", finYear) };
        return await ExecuteQueryAsync(sql, parameters, cancellationToken);
    }

    public async Task<DataTable?> GetAccountEmailAsync(string finYear, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                UK.REG_BK, UK.REG_BR, UK.REG_NO, EMAIL,
                MAX(CASE WHEN UD.CIP_FLAG = 'Y' THEN 'Y' ELSE 'N' END) CIP_FLAG
            FROM
                UNIT_KYC UK
            JOIN
                UNIT_DIVIDEND UD ON UK.REG_BK = UD.REG_BK
                                  AND UK.REG_BR = UD.REG_BR
                                  AND UK.REG_NO = UD.REG_NO
            WHERE
                UD.NET_DIVIDENT > 0
                AND UD.FIN_YEAR = :V_FIN_YEAR
                AND UK.EMAIL IS NOT NULL
                AND UK.EMAIL LIKE '%@%.%'
            GROUP BY
                UK.REG_BK, UK.REG_BR, UK.REG_NO, EMAIL
            ORDER BY
                UK.REG_BK, UK.REG_BR, UK.REG_NO";

        return await ExecuteQueryAsync(sql, new[] { new OracleParameter("V_FIN_YEAR", finYear) }, cancellationToken);
    }

    public async Task<DataTable?> GetAccountEmailByRegistrationAsync(string regBk, string regBr, string regNo, string finYear, CancellationToken cancellationToken = default)
    {
        // No email-format filter here: the single-registration mode should still find the
        // account so a missing/invalid email can be reported (or redirected by the test override).
        const string sql = @"
            SELECT
                UK.REG_BK, UK.REG_BR, UK.REG_NO, EMAIL,
                MAX(CASE WHEN UD.CIP_FLAG = 'Y' THEN 'Y' ELSE 'N' END) CIP_FLAG
            FROM
                UNIT_KYC UK
            JOIN
                UNIT_DIVIDEND UD ON UK.REG_BK = UD.REG_BK
                                  AND UK.REG_BR = UD.REG_BR
                                  AND UK.REG_NO = UD.REG_NO
            WHERE
                UD.NET_DIVIDENT > 0
                AND UD.FIN_YEAR = :V_FIN_YEAR
                AND UK.REG_BK = :V_REG_BK
                AND UK.REG_BR = :V_REG_BR
                AND UK.REG_NO = :V_REG_NO
            GROUP BY
                UK.REG_BK, UK.REG_BR, UK.REG_NO, EMAIL";

        var parameters = new[]
        {
            new OracleParameter("V_FIN_YEAR", finYear),
            new OracleParameter("V_REG_BK", regBk),
            new OracleParameter("V_REG_BR", regBr),
            new OracleParameter("V_REG_NO", regNo)
        };

        return await ExecuteQueryAsync(sql, parameters, cancellationToken);
    }

    public async Task<DataTable?> GetHolderCertificateDataAsync(string regBk, string regBr, string regNo, string finYear, CancellationToken cancellationToken = default)
    {
        // Same query as UFMS GET_HOLDER_CERTIFICATE_DATA (unitf059v1): one row
        // serves both the Income Tax and the Investment Certificate.
        const string sql = @"
            SELECT UM.REG_BK, UM.REG_BR, UM.REG_NO,
                   UM.NAME1, UM.NAME2, UM.NAME3, UM.NAME4,
                   UM.CONTACT_ADDRSS1, UM.CONTACT_ADDRSS2, UM.CONTACT_ADDRSS3,
                   UM.ETIN1,
                   TO_CHAR(UD.WARRENT_DATE, 'DD/MM/YYYY') WARRENT_DATE,
                   UD.BALANCE, UD.DIVIDEND_RATE, UD.GROSS_DIVIDEND,
                   '@Tk. ' || UD.TAX || '%' TAX_LABEL,
                   UD.TAX_DEDUCTION,
                   (UD.GROSS_DIVIDEND - UD.TAX_DEDUCTION) NET_DIVIDENT,
                   UD.FIN_YEAR, UD.YEAR_END_DATE,
                   UD.NO_OF_CIP_UNIT, UD.CIP_RATE,
                   (UD.NO_OF_CIP_UNIT * UD.CIP_RATE) AMOUNT
            FROM UNIT_KYC UM
            JOIN V_UNIT_DIVIDEND_ALL UD
                ON UM.REG_BK = UD.REG_BK
               AND UM.REG_BR = UD.REG_BR
               AND UM.REG_NO = UD.REG_NO
            WHERE UM.REG_BK = :V_REG_BK
              AND UM.REG_BR = :V_REG_BR
              AND UM.REG_NO = :V_REG_NO
              AND UD.FIN_YEAR = :V_FIN_YEAR";

        var parameters = new[]
        {
            new OracleParameter("V_REG_BK", regBk),
            new OracleParameter("V_REG_BR", regBr),
            new OracleParameter("V_REG_NO", regNo),
            new OracleParameter("V_FIN_YEAR", finYear)
        };

        return await ExecuteQueryAsync(sql, parameters, cancellationToken);
    }

    public async Task<DataTable?> GetDividendReceivableAsync(int brCd, int accntNo, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT NVL(SUM(ramount), 0) dividend_receivable
            FROM invest.brdd
            WHERE accnt_no = :accnt_no
                AND br_cd = :br_cd
                AND tr_cd IN ('IDIV', 'FDIV', 'BNC')
                AND posted = 'R'";

        var parameters = new[]
        {
            new OracleParameter("accnt_no", accntNo),
            new OracleParameter("br_cd", brCd)
        };

        return await ExecuteQueryAsync(sql, parameters, cancellationToken);
    }

    private async Task<DataTable?> ExecuteQueryAsync(string sql, OracleParameter[] parameters, CancellationToken cancellationToken)
    {
        var dt = new DataTable();
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.OpenAsync(cancellationToken);

            using var cmd = new OracleCommand(sql, conn);
            cmd.BindByName = true;
            cmd.Parameters.AddRange(parameters);

            using var adapter = new OracleDataAdapter(cmd);
            await Task.Run(() => adapter.Fill(dt), cancellationToken);

            return dt.Rows.Count > 0 ? dt : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing query: {Query}", sql[..Math.Min(100, sql.Length)]);
            throw;
        }
    }
}
