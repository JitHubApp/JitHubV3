using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using JitHubV3.Presentation.ComposeSearch;
using JitHubV3.Services.Ai;
using NUnit.Framework;

namespace JitHubV3.Tests.Ai;

public sealed class AnthropicRuntimeTests
{
    [Test]
    public async Task BuildGitHubQueryPlanAsync_ReturnsValidatedPlan_WhenResponseContainsJsonText()
    {
        var secrets = new TestSecretStore();
        await secrets.SetAsync(AiSecretKeys.AnthropicApiKey, "test-key", CancellationToken.None);

        var modelStore = new TestAiModelStore
        {
            Selection = new AiModelSelection(RuntimeId: "anthropic", ModelId: "claude-test")
        };

        var settingsStore = new TestAiRuntimeSettingsStore();

        var handler = new RecordingHttpMessageHandler(req =>
        {
            req.Headers.Contains("x-api-key").Should().BeTrue();
            req.Headers.GetValues("x-api-key").Single().Should().Be("test-key");

                        var json = """
                        {
                            "content": [
                                {
                                    "type": "text",
                                    "text": "{\"query\":\"repo:uno-platform/uno language:C# httpclient\",\"domains\":[\"code\"],\"explanation\":\"Search code for HttpClient usage.\"}"
                                }
                            ]
                        }
                        """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });

        var http = new HttpClient(handler);
        var cfg = new AnthropicRuntimeConfig { ModelId = null };
        var runtime = new AnthropicRuntime(http, secrets, modelStore, settingsStore, cfg);

        var plan = await runtime.BuildGitHubQueryPlanAsync(
            new AiGitHubQueryBuildRequest("find httpclient usages", AllowedDomains: new[] { ComposeSearchDomain.Code }),
            CancellationToken.None);

        plan.Should().NotBeNull();
        plan!.Domains.Should().Equal(new[] { ComposeSearchDomain.Code });
        plan.Query.Should().Contain("repo:uno-platform/uno");
    }
}
