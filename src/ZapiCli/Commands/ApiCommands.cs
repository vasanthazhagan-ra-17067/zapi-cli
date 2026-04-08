using Spectre.Console;
using Spectre.Console.Cli;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Api;

namespace ZapiCli.Commands;

/// <summary>
/// The <c>api</c> command group. Exposes <c>api request</c> (canonical),
/// <c>api req</c> (short alias), and <c>api call</c> (deprecated alias).
/// Command class is thin: validates flags, builds <see cref="ApiRequest"/>,
/// delegates to <see cref="ApiClient"/>, writes output via <see cref="IOutputWriter"/>.
/// No domain logic here (ADR-0007).
/// </summary>
internal static class ApiCommands
{
    // ─── api request / api req ────────────────────────────────────────────────

    public sealed class ApiRequestSettings : GlobalSettings
    {
        /// <summary>Full endpoint URL. Required (ADR-0006 — no base-URL construction).</summary>
        [CommandOption("--url <URL>")]
        public string? Url { get; init; }

        /// <summary>HTTP method: GET, POST, PUT, PATCH, DELETE (case-insensitive).</summary>
        [CommandOption("--method|-X <METHOD>")]
        public string? Method { get; init; }

        /// <summary>Inline request body (mutually exclusive with --body-file).</summary>
        [CommandOption("--body <BODY>")]
        public string? Body { get; init; }

        /// <summary>Path to a file whose contents are used as the request body.</summary>
        [CommandOption("--body-file <FILE>")]
        public string? BodyFile { get; init; }

        /// <summary>
        /// Extra request headers in <c>Key:Value</c> format. Repeatable.
        /// The Authorization header is always injected by <see cref="ApiClient"/> and must not
        /// be set here — any Authorization value in this list is silently ignored.
        /// </summary>
        [CommandOption("--header <HEADER>")]
        public List<string>? Headers { get; init; }

        /// <summary>Additional query parameters in <c>key=value</c> format. Repeatable.</summary>
        [CommandOption("--query <PARAM>")]
        public List<string>? QueryParams { get; init; }

        private static readonly HashSet<string> ValidMethods =
            ["GET", "POST", "PUT", "PATCH", "DELETE"];

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Url))
                return ValidationResult.Error("--url is required.");

            if (string.IsNullOrWhiteSpace(Method))
                return ValidationResult.Error("--method is required.");

            if (!ValidMethods.Contains(Method!.ToUpperInvariant()))
                return ValidationResult.Error(
                    $"--method '{Method}' is invalid. Allowed: GET, POST, PUT, PATCH, DELETE.");

            if (Body is not null && BodyFile is not null)
                return ValidationResult.Error("Cannot specify both --body and --body-file.");

            return ValidationResult.Success();
        }
    }

    public sealed class ApiRequestCommand : AsyncCommand<ApiRequestSettings>
    {
        private readonly ApiClient _apiClient;
        private readonly IAccountStore _accountStore;
        private readonly IOutputWriter _output;

        public ApiRequestCommand(ApiClient apiClient, IAccountStore accountStore, IOutputWriter output)
        {
            _apiClient = apiClient;
            _accountStore = accountStore;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ApiRequestSettings settings)
        {
            // Parse extra headers from --header list (split on first ':' only).
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in settings.Headers ?? [])
            {
                var colonIdx = h.IndexOf(':');
                if (colonIdx < 0)
                {
                    _output.WriteError(
                        $"Invalid header format '{h}': expected 'Key:Value'.",
                        ErrorCodes.INVALID_ARGS);
                    return 1;
                }
                headers[h[..colonIdx].Trim()] = h[(colonIdx + 1)..].Trim();
            }

            // Parse extra query params from --query list (split on first '=' only).
            var queryParams = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var q in settings.QueryParams ?? [])
            {
                var eqIdx = q.IndexOf('=');
                if (eqIdx < 0)
                {
                    _output.WriteError(
                        $"Invalid query param format '{q}': expected 'key=value'.",
                        ErrorCodes.INVALID_ARGS);
                    return 1;
                }
                queryParams[q[..eqIdx]] = q[(eqIdx + 1)..];
            }

            // Resolve body: inline string or file contents.
            string? body = settings.Body;
            if (settings.BodyFile is not null)
            {
                if (!File.Exists(settings.BodyFile))
                {
                    _output.WriteError(
                        $"Body file not found: {settings.BodyFile}",
                        ErrorCodes.IO_ERROR);
                    return 1;
                }
                body = await File.ReadAllTextAsync(settings.BodyFile).ConfigureAwait(false);
            }

            // Resolve account name: explicit --account / --account-email / --account-zuidstring or default.
            string accountName;
            if (settings.Account is not null)
            {
                var acct = await _accountStore.FindAsync(settings.Account).ConfigureAwait(false)
                    ?? throw new ZapiCliException($"Account '{settings.Account}' not found.", ErrorCodes.ACCOUNT_NOT_FOUND, 1);
                accountName = acct.Name;
            }
            else if (settings.AccountEmail is not null)
            {
                var acct = await _accountStore.FindByEmailAsync(settings.AccountEmail).ConfigureAwait(false)
                    ?? throw new ZapiCliException($"No account found with email '{settings.AccountEmail}'.", ErrorCodes.ACCOUNT_NOT_FOUND, 1);
                accountName = acct.Name;
            }
            else if (settings.AccountZuidString is not null)
            {
                var acct = await _accountStore.FindByZuidAsync(settings.AccountZuidString).ConfigureAwait(false)
                    ?? throw new ZapiCliException($"No account found with ZUID '{settings.AccountZuidString}'.", ErrorCodes.ACCOUNT_NOT_FOUND, 1);
                accountName = acct.Name;
            }
            else
            {
                var defaultAccount = await _accountStore.GetDefaultAsync().ConfigureAwait(false);
                accountName = defaultAccount.Name;
            }

            var request = new ApiRequest
            {
                Url = settings.Url!,
                Method = settings.Method!,
                Body = body,
                Headers = headers,
                QueryParams = queryParams,
                AccountName = accountName,
            };

            // Delegate the call; ZapiCliException propagates to the global exception handler.
            var response = await _apiClient.CallAsync(request).ConfigureAwait(false);

            // Always write the raw Zoho response body to stdout.
            _output.WriteRaw(response.Body);

            if (!response.IsSuccess)
            {
                // Also write an error envelope to stderr for non-2xx responses.
                _output.WriteError(
                    $"API returned {response.StatusCode}.",
                    ErrorCodes.API_ERROR,
                    exitCode: 1);
                return 1;
            }

            return 0;
        }
    }

}
