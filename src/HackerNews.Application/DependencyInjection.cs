using FluentValidation;
using HackerNews.Application.Behaviors;
using HackerNews.Application.BestStories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HackerNews.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BestStoriesOptions>()
            .Bind(configuration.GetSection(BestStoriesOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssemblyContaining<GetBestStoriesQueryValidator>(
            ServiceLifetime.Singleton,
            includeInternalTypes: true);

        // Scoped: handlers depend on typed HttpClients, which must not be captured by singletons.
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.PipelineBehaviors = [typeof(LoggingBehavior<,>), typeof(ValidationBehavior<,>)];
        });

        return services;
    }
}
