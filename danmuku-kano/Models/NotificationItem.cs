using System;

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

    private async void LoadImageAsync()
    {
        try
        {
            var stream = await AppLogoStream!.OpenReadAsync();
            await _appIconSource!.SetSourceAsync(stream);
            OnPropertyChanged(nameof(AppIconSource));
        }
        catch { }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}
