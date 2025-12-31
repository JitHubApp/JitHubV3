using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using JitHubV3.Services.Ai;
using NUnit.Framework;

namespace JitHubV3.Tests.Ai;

public sealed class AzureAiFoundryRuntimeTests
{
    [Test]
    public async Task BuildGitHubQueryPlanAsync_SendsApiKeyHeader_AndParsesResponse()
    {
        var secrets = new TestSecretStore();
        await secrets.SetAsync(AiSecretKeys.AzureAiFoundryApiKey, "foundry-key", CancellationToken.None);

      var modelStore = new TestAiModelStore
      {
        Selection = new AiModelSelection(RuntimeId: "azure-ai-foundry", ModelId: "model-test")
      };

        var handler = new RecordingHttpMessageHandler(req =>
        {
            req.Headers.Contains("api-key").Should().BeTrue();
            req.Headers.GetValues("api-key").Single().Should().Be("foundry-key");

            var json = """
            {
              "choices": [
                {"message": {"content": "{\"query\":\"repo:uno-platform/uno is:pr label:bug\",\"domain\":\"prs\"}"}}
              ]
            }
            """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.ai.azure.com") };
        var cfg = new AzureAiFoundryRuntimeConfig { Endpoint = "https://example.ai.azure.com", ModelId = null };
        var runtime = new AzureAiFoundryRuntime(http, secrets, modelStore, cfg);

        var plan = await runtime.BuildGitHubQueryPlanAsync(new AiGitHubQueryBuildRequest("find bug PRs"), CancellationToken.None);
        plan.Should().NotBeNull();
        plan!.Query.Should().Contain("repo:uno-platform/uno");
    }
}
