using Spectre.Console;
using Spectre.Console.Cli;
using ZapiCli.Core;
using ZapiCli.Core.Api;

namespace ZapiCli.Commands;

/// <summary>
/// The <c>api registry</c> command group: list, add, update, show, remove.
/// Registry operations are purely local — no active account required.
/// The host allowlist (ADR-0004) is enforced on any --url argument.
/// </summary>
internal static class ApiRegistryCommands
{
    private static readonly HashSet<string> ValidMethods =
        ["GET", "POST", "PUT", "PATCH", "DELETE"];

    // ─── api registry list ────────────────────────────────────────────────────

    public sealed class ApiRegistryListSettings : GlobalSettings { }

    public sealed class ApiRegistryListCommand : AsyncCommand<ApiRegistryListSettings>
    {
        private readonly IApiRegistry _registry;
        private readonly IOutputWriter _output;

        public ApiRegistryListCommand(IApiRegistry registry, IOutputWriter output)
        {
            _registry = registry;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ApiRegistryListSettings settings)
        {
            var root = await _registry.LoadAsync().ConfigureAwait(false);
            _output.WriteJson(root.Apis);
            return 0;
        }
    }

    // ─── api registry add ─────────────────────────────────────────────────────

    public sealed class ApiRegistryAddSettings : GlobalSettings
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

    public sealed class ApiRegistryAddCommand : AsyncCommand<ApiRegistryAddSettings>
    {
        private readonly IApiRegistry _registry;
        private readonly IOutputWriter _output;

        public ApiRegistryAddCommand(IApiRegistry registry, IOutputWriter output)
        {
            _registry = registry;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ApiRegistryAddSettings settings)
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

    // ─── api registry update ──────────────────────────────────────────────────

    public sealed class ApiRegistryUpdateSettings : GlobalSettings
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

    public sealed class ApiRegistryUpdateCommand : AsyncCommand<ApiRegistryUpdateSettings>
    {
        private readonly IApiRegistry _registry;
        private readonly IOutputWriter _output;

        public ApiRegistryUpdateCommand(IApiRegistry registry, IOutputWriter output)
        {
            _registry = registry;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ApiRegistryUpdateSettings settings)
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
                    $"API registry entry '{settings.Id}' not found.",
                    ErrorCodes.REGISTRY_ENTRY_NOT_FOUND);

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

    // ─── api registry show ────────────────────────────────────────────────────

    public sealed class ApiRegistryShowSettings : GlobalSettings
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

    public sealed class ApiRegistryShowCommand : AsyncCommand<ApiRegistryShowSettings>
    {
        private readonly IApiRegistry _registry;
        private readonly IOutputWriter _output;

        public ApiRegistryShowCommand(IApiRegistry registry, IOutputWriter output)
        {
            _registry = registry;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ApiRegistryShowSettings settings)
        {
            var entry = await _registry.FindByIdAsync(settings.Id!).ConfigureAwait(false)
                ?? throw new ZapiCliException(
                    $"API registry entry '{settings.Id}' not found.",
                    ErrorCodes.REGISTRY_ENTRY_NOT_FOUND);

            _output.WriteJson(entry);
            return 0;
        }
    }

    // ─── api registry remove ──────────────────────────────────────────────────

    public sealed class ApiRegistryRemoveSettings : GlobalSettings
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

    public sealed class ApiRegistryRemoveCommand : AsyncCommand<ApiRegistryRemoveSettings>
    {
        private readonly IApiRegistry _registry;
        private readonly IOutputWriter _output;

        public ApiRegistryRemoveCommand(IApiRegistry registry, IOutputWriter output)
        {
            _registry = registry;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ApiRegistryRemoveSettings settings)
        {
            await _registry.RemoveAsync(settings.Id!).ConfigureAwait(false);
            _output.WriteJson(new { status = "ok", data = new { id = settings.Id } });
            return 0;
        }
    }
}
