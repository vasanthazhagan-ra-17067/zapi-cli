namespace ZapiCli.Core.Api;

/// <summary>
/// Persists and queries named API endpoint entries in the local registry (<c>registry.json</c>).
/// These operations do not require an active account — they are purely local storage.
/// </summary>
public interface IApiRegistry
{
    Task<ApiRegistryRoot> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(ApiRegistryRoot root, CancellationToken ct = default);
    Task<ApiRegistryEntry?> FindByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Adds a new entry. Throws <see cref="ZapiCliException"/> with
    /// <see cref="ErrorCodes.REGISTRY_ENTRY_ALREADY_EXISTS"/> (exit 1) if the id already exists.
    /// </summary>
    Task AddAsync(ApiRegistryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Overwrites an existing entry. Throws <see cref="ZapiCliException"/> with
    /// <see cref="ErrorCodes.REGISTRY_ENTRY_NOT_FOUND"/> (exit 1) if the id is not found.
    /// </summary>
    Task UpdateAsync(ApiRegistryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Removes an entry by id. Throws <see cref="ZapiCliException"/> with
    /// <see cref="ErrorCodes.REGISTRY_ENTRY_NOT_FOUND"/> (exit 1) if the id is not found.
    /// </summary>
    Task RemoveAsync(string id, CancellationToken ct = default);
}
