# Hacker News Best Stories API

An ASP.NET Core (.NET 10) REST API that returns the best `n` stories from the
[Hacker News API](https://github.com/HackerNews/API), ordered by score descending, and is
built to serve a high request volume **without** putting that load on Hacker News.

```
GET /api/v1/stories/best?count=3
```
```json
[
  {
    "title": "A uBlock Origin update was rejected from the Chrome Web Store",
    "uri": "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
    "postedBy": "ismaildonmez",
    "time": "2019-10-12T13:43:01+00:00",
    "score": 1716,
    "commentCount": 572
  }
]
```

## Running it

Requires the .NET 10 SDK (pinned in `global.json`).

```bash
dotnet run --project src/HackerNews.Api
```

| URL | What |
| --- | --- |
| http://localhost:5080/api/v1/stories/best?count=10 | The endpoint |
| http://localhost:5080/scalar | Scalar API reference UI (Development only) |
| http://localhost:5080/openapi/v1.json | OpenAPI document (Development only) |
| http://localhost:5080/health/live | Liveness: process is up |
| http://localhost:5080/health/ready | Readiness: story cache has been warmed |

```bash
curl -i "http://localhost:5080/api/v1/stories/best?count=5"

# Conditional request: returns 304 Not Modified while the data is unchanged
curl -i -H 'If-None-Match: "<etag from previous response>"' "http://localhost:5080/api/v1/stories/best?count=5"
```

Every setting lives in [`appsettings.json`](src/HackerNews.Api/appsettings.json) and can be overridden
with environment variables, e.g. `HackerNews__ItemTtl=00:10:00` or `RateLimiting__PermitLimit=500`.
Options are validated at startup, so an inconsistent configuration fails fast.

### Tests

```bash
dotnet test --solution HackerNews.slnx

# with coverage (Cobertura, written to TestResults/)
dotnet test --solution HackerNews.slnx --coverage --coverage-output-format cobertura
```

Tests use xUnit v3 on Microsoft.Testing.Platform (enabled in `global.json`), Shouldly and NSubstitute.
Hacker News is faked with WireMock.Net in the infrastructure and integration tests, so
**no test touches the real Hacker News API**. CI (`.github/workflows/ci.yml`) builds and runs the tests on every push and PR.

## How Hacker News is protected

This is the main non-functional requirement, so it drives the design. Each layer absorbs load before it reaches the next:

```
client ──► rate limiter ──► output cache ──► handler ──► HybridCache story cache ──► resilient HttpClient ──► Hacker News
            (429)          (30 s, per n)                (single-flight, stale       (retry+jitter, timeouts,
                                                         fallback)                    circuit breaker)
                                                               ▲
                                              background refresh (every 45 s)
```

1. **Background refresh.** `BestStoriesRefreshService` warms the cache at startup and then every
   `RefreshInterval` (45 s). The ID list is refreshed on every run. An item is only refetched once it is
   older than `ItemRefreshInterval` (4 min), which is below the item TTL (5 min). Cache entries are
   overwritten *before* they expire, so user requests are served from memory and never wait on Hacker News.
   Steady-state upstream load is roughly **1 ID-list request per 45 s plus ~200 items per 4 min
   (< 1 req/s)**, independent of how much traffic the API gets.
2. **Cache** (`HybridCache`, in-process L1). The ID list is cached for 1 min and each item for 5 min.
   Missing, deleted and non-story items are cached too, so they are not refetched. Cached records are
   sealed and marked `[ImmutableObject(true)]`, so cache hits return the same instance with no deserialization.
3. **Stampede protection.** HybridCache runs one factory per key, so concurrent misses share one upstream
   call. The shared fetch is cancelled only when *every* waiting caller has cancelled: a client that
   disconnects stops waiting, but the fetch still completes for everyone else.
4. **Bounded outbound concurrency.** Item lookups use `Parallel.ForEachAsync` with
   `BestStories:MaxConcurrency` (default 8), both in the request path and in the background refresh.
5. **Resilience.** The typed `HttpClient` uses `Microsoft.Extensions.Http.Resilience`'s standard pipeline:
   retry with exponential backoff and jitter, per-attempt and total timeouts, and a circuit breaker.
6. **Stale-on-error.** Every successful fetch also stores a last-known-good copy (24 h). If Hacker News
   fails, or the circuit is open, the stale copy is served and a warning is logged. The API only returns
   `503` (with `Retry-After`) when there is no data at all. Only upstream failures trigger the fallback;
   bugs are not hidden behind stale data.
7. **HTTP caching.**
   - Output caching (30 s, varied by `count`) means repeated requests do not even reach the handler.
   - Responses carry `Cache-Control: public, max-age=30` and an `ETag`. `If-None-Match` gets a `304`.
8. **Inbound rate limiting.** A fixed window per client IP (default 100 requests/min) returns `429`
   ProblemDetails with `Retry-After`.
9. **Readiness probe.** It reports on the background refresh state (Unhealthy until warm, Degraded after
   3 missed refreshes). It never calls Hacker News, so health probes add no upstream load.

## Architecture

```
src/
  HackerNews.Domain          Story, StoryId, Score, UnixTime – invariants only, no dependencies
  HackerNews.Application     GetBestStoriesQuery + handler + validator, StoryDto, IHackerNewsClient port,
                             logging/validation pipeline behaviors
  HackerNews.Infrastructure  HN HTTP client + raw models, StoryCache, caching decorator,
                             cache warmer + background service, options
  HackerNews.Api             Minimal API endpoint, composition root, ProblemDetails, rate limiting,
                             output caching, health checks, OpenAPI/Scalar
tests/
  *.Domain.Tests / *.Application.Tests / *.Infrastructure.Tests / *.Api.IntegrationTests
```

Dependencies point inwards: Api → Infrastructure → Application → Domain.

### Why these choices

- **Pragmatic DDD.** This service is a read-only proxy with no real business logic. There are no
  aggregates, repositories, domain events or unit of work, because they would add ceremony without value.
  The Domain holds only what carries invariants:
  - `StoryId` (> 0) and `Score` (≥ 0).
  - `Story` (title and author required, comment count ≥ 0, text posts link to their HN discussion page).
  - Unix-time conversion.

  The only abstraction is the `IHackerNewsClient` port that the use case needs.
- **CQRS with Mediator.** The single use case is a query, `GetBestStoriesQuery`. Pipeline behaviors add
  cross-cutting concerns (structured logging with timing, FluentValidation) without touching the handler.
  I used [`Mediator`](https://github.com/martinothamar/Mediator) (source-generated, MIT) rather than
  MediatR, which has required a commercial licence key since v13. The API shape is nearly identical,
  and source generation removes reflection-based dispatch.
- **Minimal APIs over controllers.** There is one endpoint. Minimal APIs are the .NET 10 default and
  have less ceremony. `TypedResults` give accurate OpenAPI metadata, and route groups handle versioning
  (`/api/v1`) and per-group rate limiting. The endpoint only translates HTTP to a query.
- **Decorator for caching.** `CachingHackerNewsClient` wraps the HTTP client behind the same port. The
  handler does not know about caching, and the HTTP client does not know about caching. The background
  warmer talks to the raw HTTP client and writes into the same `StoryCache`.
- **`HybridCache` over `IMemoryCache`.** Stampede protection comes built in rather than hand-rolled.
  It runs in-process only today (no infrastructure needed). Adding a Redis L2 later is a registration
  change, with no code changes, which is the path to multiple instances.
- **Scoped Mediator lifetime.** Handlers depend on typed `HttpClient`s, which should not be captured by
  singletons. The background service creates a scope per run for the same reason.
- **`TimeProvider` everywhere time matters** (refresh scheduling, item ages, readiness, logging timings),
  tested with `FakeTimeProvider`.
- **Errors** go through `IExceptionHandler` and are returned as RFC 7807 ProblemDetails:
  - validation → 400 (with an `errors` map)
  - malformed input → 400
  - rate limit → 429
  - Hacker News unavailable with no cache → 503
  - anything else → 500 (logged)

## Assumptions

- `count` is required and must be an integer from 1 to 200. beststories.json returns up to ~200 IDs;
  the maximum is configurable via `BestStories:MaxCount`.
- Ordering is by `score` descending, with ties broken by story ID so output is stable.
- Items that are deleted, dead, not of type `story` (e.g. jobs, polls), or missing a title, author or
  time are skipped. A missing `score` or `descendants` count is treated as 0.
- Stories without an external `url` (e.g. "Ask HN") return their Hacker News discussion page
  (`https://news.ycombinator.com/item?id={id}`) as `uri`, so the field is always a usable link.
- `time` is returned as ISO 8601 with an explicit UTC offset (`+00:00`).
- **Freshness:** scores and comment counts can be up to ~5 minutes old. Add up to 30 s for the output
  cache and up to 30 s more for client/proxy caching (`max-age`). During a Hacker News outage, data can
  be up to 24 h old (stale fallback) rather than failing.
- The rate limit is per client IP. Behind a reverse proxy, forwarded headers must be enabled so the real
  client IP is used (see *Enhancements*).
- Single instance: each instance has its own cache and rate-limit counters.

## Enhancements given more time

- **Multi-instance scale-out.**
  - Register a Redis `IDistributedCache` as HybridCache's L2 so instances share one cache (and one set of upstream calls).
  - Run the background refresh on a single leader.
  - Use a distributed rate limiter.
- **Push instead of poll.** Hacker News is on Firebase, which supports streaming updates for
  `/v0/beststories` and items. That would cut upstream traffic further and make data fresher.
- **Observability.** OpenTelemetry traces and metrics: cache hit ratio, upstream latency and errors,
  circuit-breaker state, refresh duration, and 429 counts.
- **Load testing** with k6 or NBomber to validate throughput and tune TTLs, concurrency and rate limits.
- **Containerisation and orchestration.** A Dockerfile and compose file, or .NET Aspire for local
  orchestration with Redis and a dashboard.
- **Forwarded headers / API gateway** support for correct client IPs, and per-API-key quotas.
- **Stale-while-revalidate** on the request path when the background refresh is disabled.
