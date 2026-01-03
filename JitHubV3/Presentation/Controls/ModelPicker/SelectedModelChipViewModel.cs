using JitHubV3.Services.Ai.ModelPicker;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class SelectedModelChipViewModel : ObservableObject
{
    public PickerSelectedModel Model { get; }

    public string DisplayNameOrId => Model.DisplayNameOrId;

    public IRelayCommand RemoveCommand { get; }

    public SelectedModelChipViewModel(PickerSelectedModel model, Action<PickerSelectedModel> remove)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        if (remove is null)
        {
            throw new ArgumentNullException(nameof(remove));
        }

        RemoveCommand = new RelayCommand(() => remove(Model));
    }
}
