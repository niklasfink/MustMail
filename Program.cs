using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using MustMail;
using Serilog;
using Serilog.Events;
using SmtpServer;
using System.Reflection;
using System.Text.Json;
using ServiceProvider = SmtpServer.ComponentModel.ServiceProvider;

// Version and copyright message
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("Must Mail");
Console.WriteLine(Assembly.GetEntryAssembly()!.GetName().Version?.ToString(3));
Console.ForegroundColor = ConsoleColor.White;

// Configuration
IConfigurationBuilder builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

IConfiguration configuration = builder.Build();

// Get log level string
string logLevelString = configuration["LogLevel"] ?? "Information";

// Parse to Serilog log level enum
bool parsed = Enum.TryParse<LogEventLevel>(logLevelString, ignoreCase: true, out var logLevel);

if (!parsed)
{
    logLevel = LogEventLevel.Information;
}

// Creating logger
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(logLevel)
     .WriteTo.Console(
        theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Literate,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// Prase config
Configuration? config = configuration.Get<Configuration>();

// If configuration can not be parsed to config - exit
if (config == null || config.Graph == null || config.Smtp == null || config.AllowedSenders == null || config.AllowedSenders.Count == 0)
{
    Log.Error("Could not load the configuration! Please see the README for how to set the configuration!");
    Environment.Exit(1);
}

// Log configuration
Log.Information("Configuration: \n {Serialize}", JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

// Create SMTP Server options
ISmtpServerOptions? options = new SmtpServerOptionsBuilder()
    .ServerName(config.Smtp.Host)
    .Port(config.Smtp.Port, false)
    .Build();

// Create client secrete credential 
ClientSecretCredential clientSecretCredential = new(
    config.Graph.TenantId,
    config.Graph.ClientId,
    config.Graph.ClientSecret,
    new ClientSecretCredentialOptions
    {
        AuthorityHost = AzureAuthorityHosts.AzurePublicCloud
    }
);

// Create graph client
GraphServiceClient graphClient = new(clientSecretCredential, new[] { "https://graph.microsoft.com/.default" });

// Validate all allowed sender addresses
Log.Information("Validating {Count} allowed sender address(es)...", config.AllowedSenders.Count);

foreach (string senderAddress in config.AllowedSenders)
{
    try
    {
        User? user = await graphClient.Users[senderAddress].GetAsync();

        if (user == null)
        {
            Log.Error("The specified sender address: '{From}' does not exist in the tenant!", senderAddress);
            Environment.Exit(1);
        }

        if (user.Mail == null && user.UserPrincipalName == null)
        {
            Log.Error("The user '{From}' has no email address configured and cannot send mail.", senderAddress);
            Environment.Exit(1);
        }

        if (user.MailboxSettings == null)
        {
            Log.Warning("Mailbox settings for user '{From}' not found. Sending mail might not be available.", senderAddress);
        }
        else
        {
            Log.Information("Successfully validated sender address: '{From}'", senderAddress);
        }
    }
    catch (Microsoft.Graph.Models.ODataErrors.ODataError error)
    {
        Log.Error("The specified sender address: '{From}' does not exist in the tenant!\nThe Microsoft Graph error message is: '{error}'", senderAddress, error.Message);
        Environment.Exit(1);
    }
}

// Create email service provider
ServiceProvider emailServiceProvider = new();

// Add the message handler to the service provider
emailServiceProvider.Add(new MessageHandler(graphClient, Log.Logger.ForContext<MessageHandler>(), config.AllowedSenders));

// Create the server
SmtpServer.SmtpServer smtpServer = new(options, emailServiceProvider);

// Create and start health check endpoint
HealthCheckEndpoint healthEndpoint = new(config.HealthCheckPort, Log.Logger.ForContext<HealthCheckEndpoint>());

// Log server start
Log.Information("Smtp server started on {SmtpHost}:{SmtpPort}", config.Smtp.Host, config.Smtp.Port);

// Start health check endpoint in background
CancellationTokenSource cts = new();
_ = Task.Run(() => healthEndpoint.StartAsync(cts.Token), cts.Token);

// Start the SMTP server
await smtpServer.StartAsync(CancellationToken.None);


