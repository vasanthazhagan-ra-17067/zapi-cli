using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace ZapiCli;

/// <summary>
/// Bridges Microsoft.Extensions.DependencyInjection with Spectre.Console.Cli's
/// ITypeRegistrar / ITypeResolver, enabling constructor injection in command classes.
/// </summary>
internal sealed class DependencyInjectionRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _services;

    public DependencyInjectionRegistrar(IServiceCollection services)
    {
        _services = services;
    }

    public ITypeResolver Build()
    {
        return new DependencyInjectionResolver(_services.BuildServiceProvider());
    }

    public void Register(Type service, Type implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    public void RegisterInstance(Type service, object implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
        _services.AddSingleton(service, _ => factory());
    }
}

internal sealed class DependencyInjectionResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider;

    public DependencyInjectionResolver(IServiceProvider provider)
    {
        _provider = provider;
    }

    public object? Resolve(Type? type)
    {
        if (type is null) return null;
        return _provider.GetService(type);
    }

    public void Dispose()
    {
        if (_provider is IDisposable disposable)
            disposable.Dispose();
    }
}
