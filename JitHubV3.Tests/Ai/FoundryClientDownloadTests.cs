using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using JitHubV3.Services.Ai.FoundryLocal;

namespace JitHubV3.Tests.Ai;

public sealed class FoundryClientDownloadTests
{
    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public InlineProgress(Action<T> report)
        {
            _report = report;
        }

        public void Report(T value) => _report(value);
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Handler(request));
    }

    [Test]
    public async Task DownloadModel_ReportsProgress_AndParsesFinalJson()
    {
        var handler = new RoutingHandler();
        var http = new HttpClient(handler);

        handler.Handler = request =>
        {
            var uri = request.RequestUri!.ToString();

            if (uri.EndsWith("/foundry/list", StringComparison.OrdinalIgnoreCase))
            {
                var json = "[{\"name\":\"phi-4\",\"alias\":\"Phi\",\"uri\":\"asset-id\",\"providerType\":\"hf\",\"promptTemplate\":null,\"fileSizeMb\":1,\"license\":\"mit\",\"runtime\":{\"deviceType\":\"CPU\",\"executionProvider\":\"CPU\"}}]";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }

            if (uri.EndsWith("/openai/models", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            }

            if (uri.Contains("eastus.api.azureml.ms/modelregistry", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"blobSasUri\":\"https://example.test/container?sas=1\"}", Encoding.UTF8, "application/json")
                };
            }

            if (uri.StartsWith("https://example.test/container", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<Name>modelpath/</Name>", Encoding.UTF8, "application/xml")
                };
            }

            if (uri.EndsWith("/openai/download", StringComparison.OrdinalIgnoreCase) && request.Method == HttpMethod.Post)
            {
                var body = "10%\n25%\n99.5%\n{\"success\":true,\"message\":\"ok\"}\n";
                var bytes = Encoding.UTF8.GetBytes(body);
                var stream = new MemoryStream(bytes);
                var content = new StreamContent(stream);
                content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        // Create FoundryClient via reflection (constructor is private in production).
        var serviceManagerType = typeof(FoundryClient).Assembly.GetType("JitHubV3.Services.Ai.FoundryLocal.FoundryServiceManager", throwOnError: true)!;
        var serviceManager = Activator.CreateInstance(serviceManagerType, nonPublic: true)!;

        var ctor = typeof(FoundryClient).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(string), serviceManagerType, typeof(HttpClient)],
            modifiers: null);

        ctor.Should().NotBeNull();

        var client = (FoundryClient)ctor!.Invoke(["http://127.0.0.1:9999", serviceManager, http]);

        var progressValues = new List<float>();
        var progress = new InlineProgress<float>(p => progressValues.Add(p));

        var catalog = await client.ListCatalogModels();
        var model = catalog.Single();

        var result = await client.DownloadModel(model, progress, CancellationToken.None);

        result.Success.Should().BeTrue();
        progressValues.Should().NotBeEmpty();
        progressValues.Max().Should().BeGreaterThan(0.9f);
    }
}
