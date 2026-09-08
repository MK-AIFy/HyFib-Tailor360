using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tailor360.Modules.Customers.Infrastructure;
using Tailor360.Modules.Identity.Infrastructure;
using Tailor360.Platform.Persistence;
using Tailor360.Platform.Security;

namespace Tailor360.Cli;

/// <summary>
/// Builds the dependency graph the commands run against. The command line reads the same configuration
/// and secret files as the hosts, so an operator running <c>migrate</c> reaches the same database the
/// application does without repeating connection details on the command line, where they would land in
/// shell history and in the process list.
/// </summary>
public static class CliHost
{
    /// <summary>Builds a host for a command.</summary>
    public static IHost Build()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            // The content root is the directory the tool was installed to, not the directory the
            // operator happened to be standing in. Without this, running the tool from anywhere but its
            // own folder would silently find no configuration and fall back to defaults.
            ContentRootPath = AppContext.BaseDirectory,

            // A generic host reads DOTNET_ENVIRONMENT while a web host reads ASPNETCORE_ENVIRONMENT.
            // Accepting either means one variable configures the whole deployment.
            EnvironmentName = ResolveEnvironmentName(),
        });

        var secretsDirectory = builder.Configuration["Secrets:Directory"];
        if (!string.IsNullOrWhiteSpace(secretsDirectory) && Directory.Exists(secretsDirectory))
        {
            builder.Configuration.AddKeyPerFile(secretsDirectory, optional: true, reloadOnChange: false);
        }

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.TimestampFormat = "HH:mm:ss ";
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // EF Core probes for the migration history table before it exists and logs the resulting error.
        // On a first run that is expected, not a fault, and printing a stack trace there would teach an
        // operator to ignore stack traces.
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.None);

        builder.Services.AddTailor360Platform();

        // The catalogue and nothing else of the security stack: the tool seeds the roles that grant
        // permissions and validates every grant against it, and it has no request pipeline to
        // authenticate or authorise. Registering the whole of AddTailor360Security here would add a
        // cookie scheme and an anti-forgery service to a process that never serves a request.
        builder.Services.AddTailor360PermissionCatalogue();

        // The command line applies every module's migrations, so each module that owns a schema is
        // registered here as well as in the web host. A module missing from this list would have a
        // schema `migrate` never creates, and the omission would only surface when the application
        // refused to serve.
        builder.Services.AddIdentityModule(builder.Configuration);
        builder.Services.AddCustomersModule(builder.Configuration);

        return builder.Build();
    }

    /// <summary>
    /// The environment this process is running in. Defaults to production when nothing says otherwise,
    /// because an unset variable on a live server must never be read as permission to seed test data.
    /// </summary>
    public static string ResolveEnvironmentName()
        => Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
           ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
           ?? Environments.Production;
}
