namespace ZapiCli.Core.Api;

/// <summary>
/// A single named API endpoint entry stored in the local registry.
/// JSON keys follow SnakeCaseLower via the shared serializer options.
/// No secret fields — no masking required in any output.
/// </summary>
public sealed record ApiRegistryEntry
{
    public required string Id { get; init; }

    /// <summary>Full endpoint URL consistent with <c>api call --url</c> (ADR-0006).</summary>
    public required string Url { get; init; }

    public required string Method { get; init; }

    public required string Purpose { get; init; }
}
