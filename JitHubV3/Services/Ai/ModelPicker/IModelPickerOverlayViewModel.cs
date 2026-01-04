using System.Collections.ObjectModel;
using System.ComponentModel;

namespace JitHubV3.Services.Ai.ModelPicker;

public interface IModelPickerOverlayViewModel : INotifyPropertyChanged
{
    ObservableCollection<PickerSelectedModel> SelectedModels { get; }

    ModelPickerCloseReason LastCloseReason { get; }

    bool IsOpen { get; set; }

    void SetInvocation(ModelPickerInvocation invocation);

    IReadOnlyList<PickerSelectedModel> GetSelectedModelsSnapshot();
}
