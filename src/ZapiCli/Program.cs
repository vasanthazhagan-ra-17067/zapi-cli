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
using ZapiCli.Keychain;

namespace ZapiCli;

internal static class Program
{
    public static int Main(string[] args)
    {
        // Resolve env-file from persisted setting only (no --env-file flag, no CWD fallback).
        var earlyConfigDir = EncryptedFileKeychainProvider.GetDefaultConfigDir();
        var persistedEnvFile = CliSettingsStore.TryReadPersistedEnvFile(earlyConfigDir);
        if (persistedEnvFile != null)
            LoadDotEnv(persistedEnvFile);

        // Pre-read app-data-dir before DI is built (story-18 AccountStore will use this).
        var appDataDir = CliSettingsStore.TryReadPersistedAppDataDir(earlyConfigDir);

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
        var configDir = earlyConfigDir;
        services.AddSingleton<IKeychainProvider>(_ =>
            KeychainProviderFactory.Create(configDir));

        // HTTP client factory for outbound requests (used by OAuthProvider, ApiClient).
        services.AddHttpClient();

        // Account store: persists accounts.json to appDataDir (if configured) or the platform config directory.
        services.AddSingleton<IAccountStore>(sp =>
            new AccountStore(configDir, sp.GetRequiredService<ILogger<AccountStore>>(), appDataDir));

        // Auth provider: OAuth Self-Client — reads/writes keychain credential bundles.
        services.AddSingleton<IAuthProvider, OAuthProvider>();

        // Browser OAuth flow: generates CSRF state, builds authorization URLs, opens system browser.
        services.AddSingleton<IOAuthBrowserFlow, OAuthBrowserFlow>();

        // RSA key pair provider: generates fresh 2048-bit key pairs for the Mobile OAuth flow.
        services.AddSingleton<IRsaKeyPairProvider, RsaKeyPairProvider>();

        // Account service: business logic for all 'account' subcommands.
        services.AddSingleton<IAccountService, AccountService>();

        // ApiClient: dispatches authenticated HTTP calls to Zoho endpoints.
        // Named HttpClient has a 30-second timeout and is managed by IHttpClientFactory.
        services.AddHttpClient("zapi-api", client =>
            client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<ApiClient>();

        // API registry: local store of named endpoint entries for agent discovery.
        services.AddSingleton<IApiRegistry>(sp =>
            new ApiRegistry(configDir, sp.GetRequiredService<ILogger<ApiRegistry>>()));

        // Trace system: ICliSettingsStore → ITraceSession → ITraceWriter → TraceExporter.
        services.AddSingleton<ICliSettingsStore>(sp =>
            new CliSettingsStore(configDir, sp.GetRequiredService<ILogger<CliSettingsStore>>()));

        services.AddSingleton<ITraceSession>(sp =>
            new TraceSession(
                configDir,
                sp.GetRequiredService<ICliSettingsStore>(),
                sp.GetRequiredService<ILogger<TraceSession>>()));
        services.AddSingleton<ITraceWriter, TraceWriter>();
        services.AddSingleton<TraceExporter>();

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
                account.AddCommand<AccountCommands.AccountLoginCommand>("login")
                    .WithDescription("Authenticate a new Zoho account via Mobile OAuth (DC auto-detected; credentials from env).");
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
                account.AddCommand<AccountCommands.AccountRenameCommand>("rename")
                    .WithDescription("Rename an account and update its keychain entry.");
            });

            config.AddBranch("api", api =>
            {
                api.AddCommand<ApiCommands.ApiCallCommand>("call")
                    .WithDescription("Invoke a Zoho API endpoint and print the raw response.");

                api.AddBranch("registry", registry =>
                {
                    registry.AddCommand<ApiRegistryCommands.ApiRegistryListCommand>("list")
                        .WithDescription("List all entries in the local API registry.");
                    registry.AddCommand<ApiRegistryCommands.ApiRegistryAddCommand>("add")
                        .WithDescription("Add a new named API endpoint to the local registry.");
                    registry.AddCommand<ApiRegistryCommands.ApiRegistryUpdateCommand>("update")
                        .WithDescription("Update fields of an existing registry entry.");
                    registry.AddCommand<ApiRegistryCommands.ApiRegistryShowCommand>("show")
                        .WithDescription("Show a single registry entry by id.");
                    registry.AddCommand<ApiRegistryCommands.ApiRegistryRemoveCommand>("remove")
                        .WithDescription("Remove an entry from the local API registry.");
                });
            });

            config.AddBranch("util", util =>
            {
                util.AddCommand<UtilCommands.UtilTimeMsCommand>("time-ms")
                    .WithDescription("Output the current UTC time as a Unix millisecond timestamp.");
                util.AddCommand<UtilCommands.UtilUuidCommand>("uuid")
                    .WithDescription("Generate a random UUID v4.");
                util.AddCommand<UtilCommands.UtilTimeNowCommand>("time-now")
                    .WithDescription("Output the current India Standard Time (GMT+5:30) as DD/MM/YY hh:mm:ss AM/PM.");
            });

            config.AddBranch("scope", scope =>
            {
                scope.AddCommand<ScopeCommands.ScopeAddCommand>("add")
                    .WithDescription("Add one or more scopes to an account.");
                scope.AddCommand<ScopeCommands.ScopeListCommand>("list")
                    .WithDescription("List the scopes configured for an account.");
            });

            config.AddBranch("trace", trace =>
            {
                trace.AddBranch("session", session =>
                {
                    session.AddCommand<TraceCommands.StartSessionCommand>("start")
                        .WithDescription("Start a new named trace session.");
                    session.AddCommand<TraceCommands.ListSessionsCommand>("list")
                        .WithDescription("List all trace sessions.");
                    session.AddCommand<TraceCommands.ExportSessionCommand>("export")
                        .WithDescription("Export trace entries from a session.");
                    session.AddCommand<TraceCommands.CloseSessionCommand>("close")
                        .WithDescription("Close a trace session (waits for in-flight calls).");
                    session.AddCommand<TraceCommands.ReopenSessionCommand>("reopen")
                        .WithDescription("Reopen a closed session for further tracing.");
                    session.AddCommand<TraceCommands.RemoveSessionCommand>("remove")
                        .WithDescription("Remove a session entry (trace file is preserved).");
                });

                trace.AddBranch("config", traceConfig =>
                {
                    traceConfig.AddCommand<TraceCommands.TraceConfigSetCommand>("set")
                        .WithDescription("Set trace configuration (e.g. default export path).");
                    traceConfig.AddCommand<TraceCommands.TraceConfigShowCommand>("show")
                        .WithDescription("Show current trace configuration.");
                });
            });

            config.AddBranch("config", cfg =>
            {
                cfg.AddBranch("set", set =>
                {
                    set.AddCommand<ConfigCommands.ConfigSetEnvFileCommand>("env-file")
                        .WithDescription("Persist the path to a .env file loaded at every CLI startup.");
                    set.AddCommand<ConfigCommands.ConfigSetScopeFileCommand>("scope-file")
                        .WithDescription("Persist the path to a scope list file read by account login.");
                    set.AddCommand<ConfigCommands.ConfigSetAppDirCommand>("app-dir")
                        .WithDescription("Persist the path to the app data directory for accounts.json.");
                    set.AddCommand<TraceCommands.TraceConfigSetCommand>("trace-export-path")
                        .WithDescription("Set the default trace export path.");
                });
                cfg.AddCommand<ConfigCommands.ConfigShowCommand>("show")
                    .WithDescription("Show all current CLI configuration.");
            });
        });

        return app.Run(args);
    }

    /// <summary>
    /// Loads a .env file and sets any variables not already present in the environment.
    /// OS environment variables always take precedence over .env values.
    /// Lines starting with '#' and empty lines are ignored.
    /// </summary>
    private static void LoadDotEnv(string path)
    {
        if (!File.Exists(path)) return;

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var idx = trimmed.IndexOf('=');
            if (idx <= 0) continue;

            var key = trimmed[..idx].Trim();
            var value = trimmed[(idx + 1)..].Trim();

            // Strip optional surrounding quotes (single or double).
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];

            // OS environment takes precedence — only set if not already defined.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}

