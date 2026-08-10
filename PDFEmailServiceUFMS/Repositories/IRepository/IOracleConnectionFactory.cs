using Oracle.ManagedDataAccess.Client;

namespace PDFEmailServiceUFMS.Repositories.IRepository;

public interface IOracleConnectionFactory
{
    OracleConnection CreateConnection();
}
