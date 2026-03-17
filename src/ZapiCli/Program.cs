using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using ZapiCli.Commands;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Api;
using ZapiCli.Core.Auth;
using ZapiCli.Core.Trace;
using ZapiCli.Http;
using ZapiCli.Keychain;

namespace ZapiCli;

internal static class Program
{
    public static int Main(string[] args)
    {
        // Spectre.Console.Cli does not add --version built-in without a root command;
        // handle it explicitly before dispatching.
        if (args is ["--version"])
        {
            var ver = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "1.0.0";
            Console.WriteLine(ver);
            return 0;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        // Create the output writer first so the exception handler can use it
        // before the DI container is fully resolved.
        var outputWriter = new JsonOutputWriter();

        var services = new ServiceCollection();
        ConfigureServices(services, outputWriter);

        var registrar = new DependencyInjectionRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("zapi-cli");

            config.AddBranch("account", account =>
            {
                account.AddCommand<AccountAddCommand>("add")
                    .WithDescription("Add a new account.");
                account.AddCommand<AccountListCommand>("list")
                    .WithDescription("List all configured accounts.");
                account.AddCommand<AccountRemoveCommand>("remove")
                    .WithDescription("Remove an account.");
                account.AddCommand<AccountShowCommand>("show")
                    .WithDescription("Show account details.");
                account.AddCommand<AccountSetDefaultCommand>("set-default")
                    .WithDescription("Set the default account.");
                account.AddCommand<AccountReAuthCommand>("re-auth")
                    .WithDescription("Re-authenticate an account (v2).");
            });

            config.AddBranch("api", api =>
            {
                api.AddCommand<ApiCallCommand>("call")
                    .WithDescription("Invoke a Zoho product REST endpoint.");
            });

            config.AddBranch("scope", scope =>
            {
                scope.AddCommand<ScopeAddCommand>("add")
                    .WithDescription("Add a scope to an account.");
                scope.AddCommand<ScopeRemoveCommand>("remove")
                    .WithDescription("Remove a scope from an account.");
                scope.AddCommand<ScopeListCommand>("list")
                    .WithDescription("List scopes registered on an account.");
            });

            config.AddBranch("util", util =>
            {
                util.AddCommand<UtilTimeMsCommand>("time-ms")
                    .WithDescription("Output the current UTC time as a Unix millisecond timestamp.");
                util.AddCommand<UtilUuidCommand>("uuid")
                    .WithDescription("Generate a random UUID v4.");
            });

            config.AddBranch("trace", trace =>
            {
                trace.AddBranch("session", session =>
                {
                    session.AddCommand<StartSessionCommand>("start")
                        .WithDescription("Start a new named trace session.");
                    session.AddCommand<ListSessionsCommand>("list")
                        .WithDescription("List all trace sessions.");
                    session.AddCommand<ExportSessionCommand>("export")
                        .WithDescription("Export all entries from a trace session as a JSON array.");
                    session.AddCommand<CloseSessionCommand>("close")
                        .WithDescription("Close a trace session (JSONL preserved).");
                    session.AddCommand<RemoveSessionCommand>("remove")
                        .WithDescription("Remove a trace session and delete its trace files.");
                });
            });

            config.SetExceptionHandler((ex, _) =>
            {
                if (ex is OperationCanceledException)
                    return 1; // Ctrl+C — no output

                if (ex is ZapiCliException zapiEx)
                {
                    outputWriter.WriteError(zapiEx.Message, zapiEx.Code, zapiEx.ExitCode, zapiEx.Detail);
                    return zapiEx.ExitCode;
                }

                outputWriter.WriteError(ex.Message, ErrorCodes.InternalError, 1);
                return 1;
            });
        });

        return app.Run(args);
    }

    private static void ConfigureServices(IServiceCollection services, JsonOutputWriter outputWriter)
    {
        services.AddSingleton<IOutputWriter>(outputWriter);
        services.AddSingleton<IKeychainProvider>(_ => KeychainProviderFactory.Create());
        services.AddSingleton<IAccountStore, AccountStore>();
        services.AddSingleton<IAuthProvider, PatAuthProvider>();
        services.AddHttpClient();
        services.AddHttpClient<ApiClient>()
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<IUserInfoService, ZohoUserInfoService>();
        services.AddSingleton<AccountService>();
        services.AddSingleton<ScopeService>();
        services.AddSingleton<TraceSession>();
        services.AddSingleton<TraceWriter>();
        services.AddSingleton<TraceExporter>();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });
    }
}

