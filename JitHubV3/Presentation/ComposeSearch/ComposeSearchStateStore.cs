namespace JitHubV3.Presentation.ComposeSearch;

public interface IComposeSearchStateStore
{
    ComposeSearchResponse? Latest { get; }

    void SetLatest(ComposeSearchResponse? response);
}

public sealed class ComposeSearchStateStore : IComposeSearchStateStore
{
    private ComposeSearchResponse? _latest;

    public ComposeSearchResponse? Latest => _latest;

    public void SetLatest(ComposeSearchResponse? response)
    {
        _latest = response;
    }
}
