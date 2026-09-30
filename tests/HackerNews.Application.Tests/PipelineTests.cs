using FluentValidation;
using HackerNews.Application.Abstractions;
using HackerNews.Application.BestStories;
using HackerNews.Domain;
using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HackerNews.Application.Tests;

public sealed class PipelineTests : IAsyncDisposable
{
    private readonly IHackerNewsClient _client = Substitute.For<IHackerNewsClient>();
    private readonly ServiceProvider _provider;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public PipelineTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddApplication(new ConfigurationBuilder().Build());
        services.AddSingleton(_client);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public async Task Invalid_query_is_rejected_before_reaching_the_handler()
    {
        await using var scope = _provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var exception = await Should.ThrowAsync<ValidationException>(sender.Send(new GetBestStoriesQuery(0), _ct).AsTask());

        exception.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("count");
        await _client.DidNotReceiveWithAnyArgs().GetBestStoryIdsAsync(_ct);
    }

    [Fact]
    public async Task Valid_query_flows_through_to_the_handler()
    {
        var story = StoryFactory.Create(1, 99);
        _client.GetBestStoryIdsAsync(Arg.Any<CancellationToken>()).Returns([story.Id]);
        _client.GetStoryAsync(story.Id, Arg.Any<CancellationToken>()).Returns(story);
        await using var scope = _provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.Send(new GetBestStoriesQuery(1), _ct);

        result.ShouldHaveSingleItem().Score.ShouldBe(99);
    }

    [Fact]
    public async Task Handler_exceptions_propagate_through_the_pipeline()
    {
        _client.GetBestStoryIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<StoryId>>(new HttpRequestException("upstream down")));
        await using var scope = _provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await Should.ThrowAsync<HttpRequestException>(sender.Send(new GetBestStoriesQuery(1), _ct).AsTask());
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
