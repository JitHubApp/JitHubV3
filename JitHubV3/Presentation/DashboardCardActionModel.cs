using System.Windows.Input;

namespace JitHubV3.Presentation;

public sealed record DashboardCardActionModel(
    string Label,
    ICommand? Command);
