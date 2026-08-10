using Microsoft.Extensions.Configuration;
using Oracle.ManagedDataAccess.Client;
using PDFEmailServiceUFMS.Repositories.IRepository;

namespace PDFEmailServiceUFMS.Data;

public class OracleConnectionFactory : IOracleConnectionFactory
{
    private readonly string _connectionString;

    public OracleConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("OracleConnection")
            ?? throw new InvalidOperationException("Oracle connection string not found in configuration.");
    }

    public OracleConnection CreateConnection()
    {
        return new OracleConnection(_connectionString);
    }
}
