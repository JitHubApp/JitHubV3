using JitHubV3.Services.Ai;

namespace JitHubV3.Presentation;

public sealed class AiStatusBarExtension : IStatusBarExtension, IDisposable
{
    private readonly IAiEnablementStore _enablement;
    private readonly IAiModelStore _modelStore;
    private readonly IDisposable _subscription;

    private bool? _isEnabled;
    private AiModelSelection? _selection;

    public AiStatusBarExtension(
        IAiEnablementStore enablement,
        IAiModelStore modelStore,
        IAiStatusEventBus events)
    {
        _enablement = enablement ?? throw new ArgumentNullException(nameof(enablement));
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));

        if (events is null)
        {
            throw new ArgumentNullException(nameof(events));
        }

        _subscription = events.Subscribe(OnEvent);

        _ = InitializeAsync();
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<StatusBarSegment> Segments
    {
        get
        {
            var segments = new List<StatusBarSegment>(capacity: 3);

            var enabledText = _isEnabled switch
            {
                true => "AI: On",
                false => "AI: Off",
                _ => "AI: —",
            };

            segments.Add(new StatusBarSegment(
                Id: "ai-enabled",
                Text: enabledText,
                IsVisible: true,
                Priority: 120));

            var runtime = _selection?.RuntimeId;
            segments.Add(new StatusBarSegment(
                Id: "ai-runtime",
                Text: $"Runtime: {(string.IsNullOrWhiteSpace(runtime) ? "—" : runtime)}",
                IsVisible: true,
                Priority: 110));

            var model = _selection?.ModelId;
            segments.Add(new StatusBarSegment(
                Id: "ai-model",
                Text: $"Model: {(string.IsNullOrWhiteSpace(model) ? "—" : model)}",
                IsVisible: true,
                Priority: 105));

            return segments;
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            var enabled = await _enablement.GetIsEnabledAsync(CancellationToken.None).ConfigureAwait(false);
            var selection = await _modelStore.GetSelectionAsync(CancellationToken.None).ConfigureAwait(false);

            var changed = false;
            if (_isEnabled != enabled)
            {
                _isEnabled = enabled;
                changed = true;
            }

            if (!Equals(_selection, selection))
            {
                _selection = selection;
                changed = true;
            }

            if (changed)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch
        {
            // ignore
        }
    }

    private void OnEvent(AiStatusEvent evt)
    {
        switch (evt)
        {
            case AiEnablementChanged enablement:
                if (_isEnabled != enablement.IsEnabled)
                {
                    _isEnabled = enablement.IsEnabled;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
                break;

            case AiSelectionChanged selection:
                if (!Equals(_selection, selection.Selection))
                {
                    _selection = selection.Selection;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
                break;
        }
    }
}
