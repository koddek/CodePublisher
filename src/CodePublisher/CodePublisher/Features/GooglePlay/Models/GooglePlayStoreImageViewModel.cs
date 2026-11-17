using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CodePublisher.Features.GooglePlay.Images;

namespace CodePublisher.Features.GooglePlay.Models;

/// <summary>
/// One row in the Release card's store-image list. Editing a row rebuilds the immutable image
/// value the publish workflow uploads; picking a file and removing the row are owned by the view
/// model so the row can bind its own buttons.
/// </summary>
public sealed partial class GooglePlayStoreImageViewModel : ObservableObject
{
    private readonly Func<GooglePlayStoreImageViewModel, Task> _browse;
    private readonly Action<GooglePlayStoreImageViewModel> _remove;
    private readonly Action _changed;
    private GooglePlayStoreImage _image;
    private string _language;

    public GooglePlayStoreImageViewModel(
        GooglePlayStoreImage image,
        Func<GooglePlayStoreImageViewModel, Task> browse,
        Action<GooglePlayStoreImageViewModel> remove,
        Action changed)
    {
        _image = image;
        _browse = browse;
        _remove = remove;
        _changed = changed;
        _language = image.Language;
    }

    public string ImageType => _image.ImageType;

    public string Label => GooglePlayStoreImageCatalog.Describe(ImageType)?.Label ?? ImageType;

    public string Hint => GooglePlayStoreImageCatalog.Describe(ImageType)?.Hint ?? string.Empty;

    public string Path => _image.Path;

    public string FileName => _image.FileName;

    public string Language
    {
        get => _language;
        set
        {
            if (SetProperty(ref _language, value))
            {
                _changed();
            }
        }
    }

    public GooglePlayStoreImage Value => _image with { Language = _language.Trim() };

    public void SetPath(string path)
    {
        _image = _image with { Path = path };
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(FileName));
        _changed();
    }

    [RelayCommand]
    private Task BrowseAsync() => _browse(this);

    [RelayCommand]
    private void Remove() => _remove(this);
}
