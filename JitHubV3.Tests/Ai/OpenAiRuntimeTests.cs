using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using JitHubV3.Presentation.ComposeSearch;
using JitHubV3.Services.Ai;
using NUnit.Framework;

namespace JitHubV3.Tests.Ai;

public sealed class OpenAiRuntimeTests
{
    [Test]
    public async Task BuildGitHubQueryPlanAsync_ReturnsValidatedPlan_WhenResponseIsValidJson()
    {
        var secrets = new TestSecretStore();
        await secrets.SetAsync(AiSecretKeys.OpenAiApiKey, "test-key", CancellationToken.None);

        var modelStore = new TestAiModelStore
        {
            Selection = new AiModelSelection(RuntimeId: "openai", ModelId: "gpt-test")
        };

        var handler = new RecordingHttpMessageHandler(req =>
        {
            req.Headers.Authorization!.Scheme.Should().Be("Bearer");
            req.Headers.Authorization!.Parameter.Should().Be("test-key");

                        var json = """
                        {
                            "choices": [
                                {"message": {"content": "{\"query\":\"repo:uno-platform/uno is:issue bug\",\"domain\":\"issues\"}"}}
                            ]
                        }
                        """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com") };
        var cfg = new OpenAiRuntimeConfig { ModelId = null };
        var runtime = new OpenAiRuntime(http, secrets, modelStore, cfg);

        var plan = await runtime.BuildGitHubQueryPlanAsync(
            new AiGitHubQueryBuildRequest("find uno bugs", AllowedDomains: new[] { ComposeSearchDomain.IssuesAndPullRequests }),
            CancellationToken.None);

        plan.Should().NotBeNull();
        plan!.Query.Should().Contain("repo:uno-platform/uno");
        plan.Domains.Should().Equal(new[] { ComposeSearchDomain.IssuesAndPullRequests });
    }

    [Test]
    public async Task BuildGitHubQueryPlanAsync_ReturnsNull_WhenMissingApiKey()
    {
        var secrets = new TestSecretStore();

        var modelStore = new TestAiModelStore
        {
            Selection = new AiModelSelection(RuntimeId: "openai", ModelId: "gpt-test")
        };

        var http = new HttpClient(new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))
        {
            BaseAddress = new Uri("https://api.openai.com")
        };

        var cfg = new OpenAiRuntimeConfig { ModelId = null };
        var runtime = new OpenAiRuntime(http, secrets, modelStore, cfg);

        var plan = await runtime.BuildGitHubQueryPlanAsync(new AiGitHubQueryBuildRequest("x"), CancellationToken.None);
        plan.Should().BeNull();
    }
}
