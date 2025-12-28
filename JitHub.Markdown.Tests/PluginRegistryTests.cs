using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class PluginRegistryTests
{
    [Test]
    public void Create_applies_pipeline_configurators_in_plugin_registration_order_after_options()
    {
        var trace = new List<string>();

        var options = new MarkdownParserOptions
        {
            ConfigurePipeline = _ => trace.Add("options")
        };

        var engine = MarkdownEngine.Create(
            options,
            new DelegatePlugin(reg => reg.ConfigurePipeline(_ => trace.Add("A"))),
            new DelegatePlugin(reg => reg.ConfigurePipeline(_ => trace.Add("B"))));

        engine.Should().NotBeNull();
        trace.Should().Equal("options", "A", "B");
    }

    [Test]
    public void Create_preserves_renderer_registration_order_across_plugins()
    {
        var engine = MarkdownEngine.Create(
            new MarkdownParserOptions(),
            new DelegatePlugin(reg => reg.RegisterRenderer(new RendererMarker("First"))),
            new DelegatePlugin(reg => reg.RegisterRenderer(new RendererMarker("Second"))));

        engine.Plugins.Renderers.OfType<RendererMarker>().Select(x => x.Id)
            .Should().Equal("First", "Second");
    }

    [Test]
    public void Create_preserves_selection_mapper_registration_order_across_plugins()
    {
        var engine = MarkdownEngine.Create(
            new MarkdownParserOptions(),
            new DelegatePlugin(reg => reg.RegisterSelectionMapper(new MapperMarker("One"))),
            new DelegatePlugin(reg => reg.RegisterSelectionMapper(new MapperMarker("Two"))));

        engine.Plugins.SelectionMappers.OfType<MapperMarker>().Select(x => x.Id)
            .Should().Equal("One", "Two");
    }

    [Test]
    public void Create_tolerates_null_plugins()
    {
        var trace = new List<string>();

        var options = new MarkdownParserOptions
        {
            ConfigurePipeline = _ => trace.Add("options")
        };

        var engine = MarkdownEngine.Create(
            options,
            null!,
            new DelegatePlugin(reg => reg.ConfigurePipeline(_ => trace.Add("A"))));

        engine.Should().NotBeNull();
        trace.Should().Equal("options", "A");
    }

    [Test]
    public void Registry_throws_on_null_registrations_and_filters_renderers_by_type()
    {
        var reg = new MarkdownPluginRegistry();

        reg.Invoking(r => r.ConfigurePipeline(null!)).Should().Throw<ArgumentNullException>();
        reg.Invoking(r => r.RegisterRenderer<object>(null!)).Should().Throw<ArgumentNullException>();
        reg.Invoking(r => r.RegisterSelectionMapper(null!)).Should().Throw<ArgumentNullException>();

        reg.RegisterRenderer(new RendererMarker("First"));
        reg.RegisterRenderer("not-a-renderer-marker");
        reg.RegisterRenderer(new RendererMarker("Second"));

        reg.GetRenderers<RendererMarker>().Select(x => x.Id)
            .Should().Equal("First", "Second");
    }

    [Test]
    public void GitHub_enrichments_plugin_accepts_explicit_options_instance()
    {
        var options = new GitHubEnrichmentsOptions
        {
            BaseUrl = "https://github.com",
            RepositorySlug = "uno-platform/uno",
        };

        var engine = MarkdownEngine.Create(
            new MarkdownParserOptions(),
            new GitHubEnrichmentsPlugin(options));

        engine.Parse("Ping @someone").Should().NotBeNull();

        // Also cover the default (null options) constructor branch.
        var engine2 = MarkdownEngine.Create(
            new MarkdownParserOptions(),
            new GitHubEnrichmentsPlugin());

        engine2.Parse("Ping @someone").Should().NotBeNull();
    }

    private sealed class DelegatePlugin : IMarkdownRenderPlugin
    {
        private readonly Action<MarkdownPluginRegistry> _register;

        public DelegatePlugin(Action<MarkdownPluginRegistry> register)
        {
            _register = register;
        }

        public void Register(MarkdownPluginRegistry registry) => _register(registry);
    }

    private sealed class RendererMarker
    {
        public RendererMarker(string id) => Id = id;

        public string Id { get; }
    }

    private sealed class MapperMarker : ISelectionSourceIndexMapper
    {
        public MapperMarker(string id) => Id = id;

        public string Id { get; }

        public bool TryMapCaretToSourceIndex(
            string sourceMarkdown,
            MarkdownDocumentModel document,
            InlineRunLayout run,
            int caretTextOffset,
            out int sourceIndex)
        {
            sourceIndex = -1;
            return false;
        }
    }
}
