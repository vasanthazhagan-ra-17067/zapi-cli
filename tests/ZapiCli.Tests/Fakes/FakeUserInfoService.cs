using ZapiCli.Core.Auth;

namespace ZapiCli.Tests.Fakes;

/// <summary>
/// Fake implementation of <see cref="IUserInfoService"/> for unit tests.
/// Returns the pre-configured email without making any HTTP calls.
/// </summary>
public sealed class FakeUserInfoService : IUserInfoService
{
    private readonly string? _email;

    public FakeUserInfoService(string? email) => _email = email;

    public Task<string?> GetEmailAsync(string domain, string token, CancellationToken ct = default) =>
        Task.FromResult(_email);
}
