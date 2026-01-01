using System.Collections.ObjectModel;

namespace JitHubV3.Presentation.Controls.ModelPicker;

public sealed partial class ModelOrApiPickerViewModel : ObservableObject
{
    public ObservableCollection<ModelPickerCategoryItem> Categories { get; } = new();

    private ModelPickerCategoryItem? _selectedCategory;
    public ModelPickerCategoryItem? SelectedCategory
    {
        get => _selectedCategory;
        set => SetProperty(ref _selectedCategory, value);
    }

    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }

    public IRelayCommand ApplyCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public ModelOrApiPickerViewModel()
    {
        Categories.Add(new ModelPickerCategoryItem(Id: "models", DisplayName: "Models"));
        Categories.Add(new ModelPickerCategoryItem(Id: "apis", DisplayName: "APIs"));

        SelectedCategory = Categories.FirstOrDefault();

        ApplyCommand = new RelayCommand(() => IsOpen = false);
        CancelCommand = new RelayCommand(() => IsOpen = false);
    }
}
