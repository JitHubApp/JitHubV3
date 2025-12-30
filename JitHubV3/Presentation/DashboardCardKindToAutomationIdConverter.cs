using Microsoft.UI.Xaml.Data;

namespace JitHubV3.Presentation;

public sealed class DashboardCardKindToAutomationIdConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DashboardCardKind kind)
        {
            return kind switch
            {
                DashboardCardKind.SelectedRepo => "DashboardCard.SelectedRepo",
                DashboardCardKind.RepoIssuesSummary => "DashboardCard.RepoIssuesSummary",
                DashboardCardKind.RepoRecentlyUpdatedIssues => "DashboardCard.RepoRecentlyUpdatedIssues",
                DashboardCardKind.MyAssignedWork => "DashboardCard.MyAssignedWork",
                DashboardCardKind.MyReviewRequests => "DashboardCard.MyReviewRequests",
                DashboardCardKind.Notifications => "DashboardCard.Notifications",
                DashboardCardKind.MyRecentActivity => "DashboardCard.MyRecentActivity",
                DashboardCardKind.RepoRecentActivity => "DashboardCard.RepoRecentActivity",
                DashboardCardKind.RepoSnapshot => "DashboardCard.RepoSnapshot",
                _ => "DashboardCard.Unknown",
            };
        }

        return "DashboardCard.Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
