using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace JitHubV3.Presentation.Controls.ModelPicker;

/// <summary>
/// Phase 4.1 (execution plan): select the right pane template by a string key.
/// This aligns the host with a definition/registry approach where panes are identified by Id/template key.
/// </summary>
public sealed class PickerPaneTemplateSelector : DataTemplateSelector
{
    protected override DataTemplate? SelectTemplateCore(object item) => SelectTemplateImpl(item, null);

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateImpl(item, container);

    private static DataTemplate? SelectTemplateImpl(object item, DependencyObject? container)
    {
        if (item is not PickerCategoryViewModel pane)
        {
            return null;
        }

        var key = pane.TemplateKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        // Prefer local resources (templates are currently defined on the overlay root).
        var fromTree = TryFindResourceUpTree(container, key) as DataTemplate;
        if (fromTree is not null)
        {
            return fromTree;
        }

        // Fallback to app-level resources if templates are later centralized.
        return Application.Current.Resources.TryGetValue(key, out var appResource) ? appResource as DataTemplate : null;
    }

    private static object? TryFindResourceUpTree(DependencyObject? start, string key)
    {
        var current = start;
        while (current is not null)
        {
            if (current is FrameworkElement fe)
            {
                if (fe.Resources.TryGetValue(key, out var resource))
                {
                    return resource;
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
