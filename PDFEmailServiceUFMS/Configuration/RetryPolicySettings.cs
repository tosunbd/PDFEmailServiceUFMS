namespace PDFEmailServiceUFMS.Configuration;

public class RetryPolicySettings
{
    public int MaxRetryAttempts { get; set; } = 3;
    public int InitialDelaySeconds { get; set; } = 2;
    public int MaxDelaySeconds { get; set; } = 30;
    public double BackoffMultiplier { get; set; } = 2.0;
}
