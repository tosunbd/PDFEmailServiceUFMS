namespace PDFEmailServiceUFMS.Repositories.IRepository;

public interface IEmailWorkflowService
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
