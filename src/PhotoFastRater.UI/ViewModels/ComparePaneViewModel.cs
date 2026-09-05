using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoFastRater.UI.ViewModels;

/// <summary>Contains one bounded comparison pane and its independent viewport state.</summary>
public partial class ComparePaneViewModel(PhotoViewModel photo) : ViewModelBase
{
    /// <summary>Gets the stable source photo.</summary>
    public PhotoViewModel Photo { get; } = photo;

    [ObservableProperty]
    private BitmapImage? _image;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private double _zoom = 1;
}
