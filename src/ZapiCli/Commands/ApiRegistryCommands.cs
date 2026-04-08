using Spectre.Console;
using Spectre.Console.Cli;
using ZapiCli.Core;
using ZapiCli.Core.Api;

namespace ZapiCli.Commands;

/// <summary>
/// The <c>api endpoints</c> command group: list, add, update, show, remove.
/// Registry operations are purely local — no active account required.
/// The host allowlist (ADR-0004) is enforced on any --url argument.
/// </summary>
internal static class ApiRegistryCommands
{
    private static readonly HashSet<string> ValidMethods =
        ["GET", "POST", "PUT", "PATCH", "DELETE"];

    // ─── api endpoints list ───────────────────────────────────────────────────

    public sealed class ApiEndpointListSettings : GlobalSettings { }

    public sealed class ApiEndpointListCommand : AsyncCommand<ApiEndpointListSettings>
    {
        private readonly IApiRegistry _registry;
        private readonly IOutputWriter _output;

        public ApiEndpointListCommand(IApiRegistry registry, IOutputWriter output)
        {
            _registry = registry;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ApiEndpointListSettings settings)
        {
            var root = await _registry.LoadAsync().ConfigureAwait(false);
            _output.WriteJson(root.Apis);
            return 0;
        }
    }

    // ─── api endpoints add ────────────────────────────────────────────────────

    public sealed class ApiEndpointAddSettings : GlobalSettings
    {
        [CommandOption("--id <ID>")]
        public string? Id { get; init; }

        [CommandOption("--url <URL>")]
        public string? Url { get; init; }

        [CommandOption("--method <METHOD>")]
        public string? Method { get; init; }

        [CommandOption("--purpose <PURPOSE>")]
        public string? Purpose { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Id))
                return ValidationResult.Error("--id is required.");

            if (string.IsNullOrWhiteSpace(Url))
                return ValidationResult.Error("--url is required.");

            if (string.IsNullOrWhiteSpace(Method))
                return ValidationResult.Error("--method is required.");

            if (string.IsNullOrWhiteSpace(Purpose))
                return ValidationResult.Error("--purpose is required.");

            if (!ValidMethods.Contains(Method!.ToUpperInvariant()))
                return ValidationResult.Error(
                    $"--method '{Method}' is invalid. Allowed: GET, POST, PUT, PATCH, DELETE.");

            return ValidationResult.Success();
        }
    }

    public sealed class ApiEndpointAddCommand : AsyncCommand<ApiEndpointAddSettings>
    {
        private readonly IApiRegistry _registry;
        private readonly IOutputWriter _output;

        public ApiEndpointAddCommand(IApiRegistry registry, IOutputWriter output)
        {
            _registry = registry;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ApiEndpointAddSettings settings)
        {
            // Host allowlist check (ADR-0004) — must yield HOST_NOT_ALLOWED, not INVALID_ARGS.
            Uri uri;
            try
            {
                uri = new Uri(settings.Url!);
            }
            catch (UriFormatException)
            {
                throw new ZapiCliException(
                    $"--url '{settings.Url}' is not a valid URL.",
                    ErrorCodes.INVALID_ARGS);
            }

            HostValidator.ValidateHost(uri);

            var entry = new ApiRegistryEntry
            {
                Id = settings.Id!,
                Url = settings.Url!,
                Method = settings.Method!.ToUpperInvariant(),
                Purpose = settings.Purpose!,
            };

            await _registry.AddAsync(entry).ConfigureAwait(false);
            _output.WriteJson(new { status = "ok", data = new { id = settings.Id } });
            return 0;
        }
    }

    // ─── api endpoints update ─────────────────────────────────────────────────

    public sealed class ApiEndpointUpdateSettings : GlobalSettings
    {
        [CommandOption("--id <ID>")]
        public string? Id { get; init; }

        [CommandOption("--url <URL>")]
        public string? Url { get; init; }

        [CommandOption("--method <METHOD>")]
        public string? Method { get; init; }

        [CommandOption("--purpose <PURPOSE>")]
        public string? Purpose { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Id))
                return ValidationResult.Error("--id is required.");

            if (Url is null && Method is null && Purpose is null)
                return ValidationResult.Error(
                    "At least one of --url, --method, or --purpose must be provided.");

            if (Method is not null && !ValidMethods.Contains(Method.ToUpperInvariant()))
                return ValidationResult.Error(
                    $"--method '{Method}' is invalid. Allowed: GET, POST, PUT, PATCH, DELETE.");

            return ValidationResult.Success();
        }
    }

    public sealed class ApiEndpointUpdateCommand : AsyncCommand<ApiEndpointUpdateSettings>
    {
        private readonly IApiRegistry _registry;
        private readonly IOutputWriter _output;

        public ApiEndpointUpdateCommand(IApiRegistry registry, IOutputWriter output)
        {
            _registry = registry;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ApiEndpointUpdateSettings settings)
        {
            // Host allowlist check for --url if provided (ADR-0004).
            if (settings.Url is not null)
            {
                Uri uri;
                try
                {
                    uri = new Uri(settings.Url);
                }
                catch (UriFormatException)
                {
                    throw new ZapiCliException(
                        $"--url '{settings.Url}' is not a valid URL.",
                        ErrorCodes.INVALID_ARGS);
                }

                HostValidator.ValidateHost(uri);
            }

            var existing = await _registry.FindByIdAsync(settings.Id!).ConfigureAwait(false)
                ?? throw new ZapiCliException(
                    $"API endpoint '{settings.Id}' not found.",
                    ErrorCodes.ENDPOINT_NOT_FOUND);

            // Apply only the provided fields; keep existing values for omitted fields.
            var updated = existing with
            {
                Url = settings.Url ?? existing.Url,
                Method = settings.Method is not null
                    ? settings.Method.ToUpperInvariant()
                    : existing.Method,
                Purpose = settings.Purpose ?? existing.Purpose,
            };

            await _registry.UpdateAsync(updated).ConfigureAwait(false);
            _output.WriteJson(new { status = "ok", data = new { id = settings.Id } });
            return 0;
        }
    }

    // ─── api endpoints show ───────────────────────────────────────────────────

    public sealed class ApiEndpointShowSettings : GlobalSettings
    {
        [CommandOption("--id <ID>")]
        public string? Id { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Id))
                return ValidationResult.Error("--id is required.");
            return ValidationResult.Success();
        }
    }

    public sealed class ApiEndpointShowCommand : AsyncCommand<ApiEndpointShowSettings>
    {
        private readonly IApiRegistry _registry;
        private readonly IOutputWriter _output;

        public ApiEndpointShowCommand(IApiRegistry registry, IOutputWriter output)
        {
            _registry = registry;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ApiEndpointShowSettings settings)
        {
            var entry = await _registry.FindByIdAsync(settings.Id!).ConfigureAwait(false)
                ?? throw new ZapiCliException(
                    $"API endpoint '{settings.Id}' not found.",
                    ErrorCodes.ENDPOINT_NOT_FOUND);

            _output.WriteJson(entry);
            return 0;
        }
    }

    // ─── api endpoints remove ─────────────────────────────────────────────────

    public sealed class ApiEndpointRemoveSettings : GlobalSettings
    {
        [CommandOption("--id <ID>")]
        public string? Id { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Id))
                return ValidationResult.Error("--id is required.");
            return ValidationResult.Success();
        }
    }

    public sealed class ApiEndpointRemoveCommand : AsyncCommand<ApiEndpointRemoveSettings>
    {
        private readonly IApiRegistry _registry;
        private readonly IOutputWriter _output;

        public ApiEndpointRemoveCommand(IApiRegistry registry, IOutputWriter output)
        {
            _registry = registry;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ApiEndpointRemoveSettings settings)
        {
            await _registry.RemoveAsync(settings.Id!).ConfigureAwait(false);
            _output.WriteJson(new { status = "ok", data = new { id = settings.Id } });
            return 0;
        }
    }

}
