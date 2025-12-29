namespace JitHubV3.Presentation;

public sealed partial class SecondViewModel
{
	private readonly INavigator _navigator;

	public SecondViewModel(Entity entity, INavigator navigator)
	{
		Entity = entity;
		_navigator = navigator;
		GoBack = new AsyncRelayCommand(DoGoBack);
	}

	public Entity Entity { get; }

	public ICommand GoBack { get; }

	private Task DoGoBack(CancellationToken ct)
		=> _navigator.NavigateBackAsync(this, cancellation: ct);
}
