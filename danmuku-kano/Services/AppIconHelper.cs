using System;
using System.Drawing;
using System.IO;
using Microsoft.UI.Xaml;

namespace damuku_kano.Services;

internal static class AppIconHelper
{
    private static Icon? _cachedIcon;

    public static void ApplyWindowIcon(Window window)
    {
        try
        {
            var iconId = Microsoft.UI.Win32Interop.GetIconIdFromIcon(GetApplicationIcon().Handle);
            window.AppWindow.SetIcon(iconId);
        }
        catch
        {
        }
    }

    public static Icon GetApplicationIcon()
    {
        if (_cachedIcon != null)
        {
            return _cachedIcon;
        }

        try
        {
            string pngPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "B_1720288456383.png");
            if (File.Exists(pngPath))
            {
                using var bitmap = new Bitmap(pngPath);
                _cachedIcon = Icon.FromHandle(bitmap.GetHicon());
                return _cachedIcon;
            }
        }
        catch
        {
        }

        _cachedIcon = SystemIcons.Application;
        return _cachedIcon;
    }
}
