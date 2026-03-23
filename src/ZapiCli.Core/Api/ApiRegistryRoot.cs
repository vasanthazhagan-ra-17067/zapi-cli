namespace ZapiCli.Core.Api;

/// <summary>Root container for <c>registry.json</c>.</summary>
public sealed record ApiRegistryRoot
{
    public List<ApiRegistryEntry> Apis { get; init; } = [];
}
