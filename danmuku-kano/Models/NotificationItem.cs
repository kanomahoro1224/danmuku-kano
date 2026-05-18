using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace damuku_kano.Models;

public class NotificationItem : System.ComponentModel.INotifyPropertyChanged
{
    public uint Id { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public Windows.Storage.Streams.RandomAccessStreamReference? AppLogoStream { get; set; }

    private Microsoft.UI.Xaml.Media.Imaging.BitmapImage? _appIconSource;
    public Microsoft.UI.Xaml.Media.ImageSource? AppIconSource
    {
        get
        {
            if (_appIconSource != null) return _appIconSource;
            if (AppLogoStream != null)
            {
                _appIconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                LoadImageAsync();
            }
            return _appIconSource;
        }
    }

    private bool _isLoadingImage;

    private async void LoadImageAsync()
    {
        if (_isLoadingImage) return;
        _isLoadingImage = true;
        try
        {
            if (AppLogoStream == null || _appIconSource == null) return;
            var stream = await AppLogoStream.OpenReadAsync();
            await _appIconSource.SetSourceAsync(stream);
            OnPropertyChanged(nameof(AppIconSource));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load app icon: {ex.Message}");
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}
