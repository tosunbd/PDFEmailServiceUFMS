using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PDFEmailServiceUFMS.Configuration;
using PDFEmailServiceUFMS.Data;
using PDFEmailServiceUFMS.Repositories;
using PDFEmailServiceUFMS.Repositories.IRepository;
using PDFEmailServiceUFMS.Services;
using PDFEmailServiceUFMS.UI;
using Serilog;

namespace PDFEmailServiceUFMS;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
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

            // Register UI
            builder.Services.AddTransient<MainForm>();

            // The host is only used as a DI/configuration/logging container -
            // the WinForms message loop below drives the application instead.
            using var host = builder.Build();

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(host.Services.GetRequiredService<MainForm>());
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "UFMS Tax & Investment Certificate Email Service terminated unexpectedly");
            MessageBox.Show(ex.ToString(), "UFMS Certificate Email Service - Fatal Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
