namespace JitHubV3.Presentation;

public interface IActivatableViewModel
{
    Task ActivateAsync();

    void Deactivate();
}
