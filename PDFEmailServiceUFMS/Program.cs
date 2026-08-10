using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PDFEmailServiceUFMS.Configuration;
using PDFEmailServiceUFMS.Data;
using PDFEmailServiceUFMS.Repositories;
using PDFEmailServiceUFMS.Repositories.IRepository;
using PDFEmailServiceUFMS.Services;
using Serilog;

// Configure Serilog early for startup logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting UFMS Tax & Investment Certificate Email Service");

    var builder = Host.CreateApplicationBuilder(args);

    // Configure Serilog from configuration
    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: "logs/ufmscertemailservice-.log",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{ThreadId}] {Message:lj}{NewLine}{Exception}"));

    // Configure options
    builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
    builder.Services.Configure<RetryPolicySettings>(builder.Configuration.GetSection("RetryPolicy"));
    builder.Services.Configure<ApplicationSettings>(builder.Configuration.GetSection("Application"));

    // Register data access
    builder.Services.AddSingleton<IOracleConnectionFactory, OracleConnectionFactory>();

    // Register repositories
    builder.Services.AddScoped<IUnitFundRepository, UnitFundRepository>();

    // Register services
    builder.Services.AddScoped<IEmailService, MailKitEmailService>();
    builder.Services.AddScoped<IPdfGenerationService, PdfGenerationService>();
    builder.Services.AddScoped<IEmailWorkflowService, EmailWorkflowService>();

    // Register hosted service
    builder.Services.AddHostedService<EmailWorkerService>();

    var host = builder.Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "UFMS Tax & Investment Certificate Email Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
