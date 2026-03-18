using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using ZapiCli.Commands;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Api;
using ZapiCli.Core.Auth;
using ZapiCli.Keychain;

namespace ZapiCli;

internal static class Program
{
    public static int Main(string[] args)
    {
        // Handle --version before dispatching to Spectre — CommandApp has no root command.
        if (args is ["--version"])
        {
            var ver = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "1.0.0";
            Console.WriteLine(ver);
            return 0;
        }

        var services = new ServiceCollection();

        // Output writer: all command output must flow through this — never Console directly.
        services.AddSingleton<IOutputWriter, JsonOutputWriter>();

        // Keychain: select best available provider for this platform.
        var configDir = EncryptedFileKeychainProvider.GetDefaultConfigDir();
        services.AddSingleton<IKeychainProvider>(_ =>
            KeychainProviderFactory.Create(configDir));

        // HTTP client factory for outbound requests (used by OAuthProvider, ApiClient).
        services.AddHttpClient();

        // Account store: persists accounts.json to the platform config directory.
        services.AddSingleton<IAccountStore>(sp =>
            new AccountStore(configDir, sp.GetRequiredService<ILogger<AccountStore>>()));

        // Auth provider: OAuth Self-Client — reads/writes keychain credential bundles.
        services.AddSingleton<IAuthProvider, OAuthProvider>();

        // Account service: business logic for all 'account' subcommands.
        services.AddSingleton<IAccountService, AccountService>();

        // ApiClient: dispatches authenticated HTTP calls to Zoho endpoints.
        // Named HttpClient has a 30-second timeout and is managed by IHttpClientFactory.
        services.AddHttpClient("zapi-api", client =>
            client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<ApiClient>();

        // Logging: Warning+ to stderr only so the JSON stdout contract is never broken.
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });
        });

        var registrar = new DependencyInjectionRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(config =>
        {
            config.SetApplicationName("zapi-cli");

            config.SetExceptionHandler((ex, resolver) =>
            {
                var writer = resolver?.Resolve(typeof(IOutputWriter)) as IOutputWriter
                             ?? new JsonOutputWriter();

                // Ctrl+C: exit 1, no output.
                if (ex is OperationCanceledException)
                    return 1;

                if (ex is ZapiCliException zapiEx)
                {
                    writer.WriteError(zapiEx.Message, zapiEx.Code, zapiEx.ExitCode);
                    return zapiEx.ExitCode;
                }

                // Spectre fires CommandRuntimeException when Validate() returns an error.
                if (ex is Spectre.Console.Cli.CommandRuntimeException)
                {
                    writer.WriteError(ex.Message, ErrorCodes.INVALID_ARGS, 1);
                    return 1;
                }

                writer.WriteError(ex.Message, ErrorCodes.INTERNAL_ERROR, 1);
                return 1;
            });

            config.AddBranch("account", account =>
            {
                account.AddCommand<AccountCommands.AccountAddCommand>("add")
                    .WithDescription("Add a new Zoho account (OAuth Self-Client).");
                account.AddCommand<AccountCommands.AccountListCommand>("list")
                    .WithDescription("List all configured accounts.");
                account.AddCommand<AccountCommands.AccountShowCommand>("show")
                    .WithDescription("Show details of an account (token masked).");
                account.AddCommand<AccountCommands.AccountSetDefaultCommand>("set-default")
                    .WithDescription("Set the default account.");
                account.AddCommand<AccountCommands.AccountRemoveCommand>("remove")
                    .WithDescription("Remove an account and revoke its token.");
                account.AddCommand<AccountCommands.AccountReAuthCommand>("re-auth")
                    .WithDescription("Re-authenticate an account using stored credentials.");
            });

            config.AddBranch("api", api =>
            {
                api.AddCommand<ApiCommands.ApiCallCommand>("call")
                    .WithDescription("Invoke a Zoho API endpoint and print the raw response.");
            });

            config.AddBranch("util", util =>
            {
                util.AddCommand<UtilCommands.UtilTimeMsCommand>("time-ms")
                    .WithDescription("Output the current UTC time as a Unix millisecond timestamp.");
                util.AddCommand<UtilCommands.UtilUuidCommand>("uuid")
                    .WithDescription("Generate a random UUID v4.");
            });
        });

        return app.Run(args);
    }
}

