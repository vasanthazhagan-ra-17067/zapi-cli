using System.ComponentModel;
using System.Text.Json;
using Spectre.Console.Cli;
using ZapiCli.Core;
using ZapiCli.Core.Api;
using ZapiCli.Core.Accounts;

namespace ZapiCli.Commands;

// ─── Settings ────────────────────────────────────────────────────────────────

public sealed class ApiCallSettings : GlobalSettings
{
    [CommandOption("--base-url")]
    [Description("Base URL of the Zoho product API, e.g. https://cliq.zoho.com/api/v2.")]
    public required string BaseUrl { get; init; }

    [CommandOption("--method|-m")]
    [Description("HTTP method: GET, POST, PUT, PATCH, or DELETE.")]
    public required string Method { get; init; }

    [CommandOption("--path|-p")]
    [Description("API path to append to --base-url, e.g. /channels.")]
    public required string Path { get; init; }

    [CommandOption("--body|-b")]
    [Description("JSON request body (mutually exclusive with --body-file).")]
    public string? Body { get; init; }

    [CommandOption("--body-file")]
    [Description("Path to a file containing the JSON request body (mutually exclusive with --body).")]
    public string? BodyFile { get; init; }

    [CommandOption("--header|-H")]
    [Description("Additional header in 'key:value' format. Can be repeated.")]
    public string[]? Headers { get; init; }

    [CommandOption("--query|-q")]
    [Description("Query parameter in 'key=value' format. Can be repeated.")]
    public string[]? QueryParams { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        if (Body is not null && BodyFile is not null)
            return Spectre.Console.ValidationResult.Error(
                "--body and --body-file are mutually exclusive. Provide only one.");

        return Spectre.Console.ValidationResult.Success();
    }
}

// ─── Command ──────────────────────────────────────────────────────────────────

public sealed class ApiCallCommand : AsyncCommand<ApiCallSettings>
{
    private readonly ApiClient _apiClient;
    private readonly IAccountStore _accountStore;
    private readonly IOutputWriter _output;

    public ApiCallCommand(ApiClient apiClient, IAccountStore accountStore, IOutputWriter output)
    {
        _apiClient = apiClient;
        _accountStore = accountStore;
        _output = output;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ApiCallSettings settings)
    {
        // Resolve body: prefer --body, fall back to --body-file.
        string? body = settings.Body;
        if (body is null && settings.BodyFile is not null)
            body = await File.ReadAllTextAsync(settings.BodyFile);

        // Parse headers: "key:value"
        var headers = new Dictionary<string, string>();
        if (settings.Headers is not null)
        {
            foreach (var header in settings.Headers)
            {
                var idx = header.IndexOf(':');
                if (idx < 1)
                    throw new ZapiCliException(
                        $"Invalid header format '{header}'. Expected 'key:value'.",
                        ErrorCodes.InvalidArgs, exitCode: 1);
                headers[header[..idx].Trim()] = header[(idx + 1)..].Trim();
            }
        }

        // Parse query parameters: "key=value"
        var queryParams = new Dictionary<string, string>();
        if (settings.QueryParams is not null)
        {
            foreach (var param in settings.QueryParams)
            {
                var idx = param.IndexOf('=');
                if (idx < 1)
                    throw new ZapiCliException(
                        $"Invalid query parameter format '{param}'. Expected 'key=value'.",
                        ErrorCodes.InvalidArgs, exitCode: 1);
                queryParams[param[..idx]] = param[(idx + 1)..];
            }
        }

        var request = new ApiRequest
        {
            BaseUrl = settings.BaseUrl,
            Method = settings.Method.ToUpperInvariant(),
            Path = settings.Path,
            Body = body,
            Headers = headers,
            QueryParams = queryParams,
            AccountName = settings.Account,
        };

        var response = await _apiClient.CallAsync(request);

        // Deserialise the raw JSON body as a JsonElement so the 'data' envelope contains
        // real JSON (not a string-escaped value).
        var data = JsonSerializer.Deserialize<JsonElement>(response.Body);
        _output.WriteSuccess(data);
        return 0;
    }
}
