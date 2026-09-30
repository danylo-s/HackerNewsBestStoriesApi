using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HackerNews.Infrastructure.Tests;

internal static class TestServices
{
    /// <summary>Builds the real Infrastructure composition with test-friendly option overrides.</summary>
    public static ServiceProvider Build(Action<HackerNewsOptions> configure, Action<IServiceCollection>? customize = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddInfrastructure(new ConfigurationBuilder().Build());
        services.Configure<HackerNewsOptions>(options =>
        {
            options.RefreshEnabled = false;
            options.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
            configure(options);
        });
        customize?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
