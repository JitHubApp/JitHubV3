using CommunityToolkit.Mvvm.ComponentModel;
using JitHub.GitHub.Abstractions.Models;

namespace JitHubV3.Presentation;

public sealed partial class DashboardContext : ObservableObject
{
    private RepoKey? _selectedRepo;

    public RepoKey? SelectedRepo
    {
        get => _selectedRepo;
        set => SetProperty(ref _selectedRepo, value);
    }
}
