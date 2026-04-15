using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using damuku_kano.Models;
using Microsoft.UI.Dispatching;

namespace damuku_kano.Services;

public sealed class NotificationService : IDisposable
{
    private UserNotificationListener? _listener;
    private readonly DispatcherQueue _dispatcherQueue;
    private bool _isInitialized;
    private bool _isDisposed;

    public ObservableCollection<NotificationItem> History { get; } = new ObservableCollection<NotificationItem>();
    public event Action<NotificationItem>? OnNewDanmaku;

    public NotificationService()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        if (_isDisposed || _isInitialized)
        {
            return;
        }

        _listener = UserNotificationListener.Current;
        var accessStatus = await _listener.RequestAccessAsync();

        if (accessStatus != UserNotificationListenerAccessStatus.Allowed)
        {
            return;
        }

        _listener.NotificationChanged += Listener_NotificationChanged;
        _isInitialized = true;

        try
        {
            var notifs = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in notifs.OrderByDescending(x => x.CreationTime).Take(20))
            {
                AddNotificationToList(n);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_listener != null && _isInitialized)
        {
            _listener.NotificationChanged -= Listener_NotificationChanged;
        }

        _isDisposed = true;
        _isInitialized = false;
        OnNewDanmaku = null;
    }

    private void Listener_NotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (_isDisposed)
        {
            return;
        }

        if (args.ChangeKind == UserNotificationChangedKind.Added)
        {
            try
            {
                var notif = _listener?.GetNotification(args.UserNotificationId);
                if (notif != null)
                {
                    var item = AddNotificationToList(notif);
                    if (item != null)
                    {
                        _dispatcherQueue.TryEnqueue(() => OnNewDanmaku?.Invoke(item));
                    }
                }
            } 
            catch { }
        }
    }

    private NotificationItem? AddNotificationToList(UserNotification notif)
    {
        var bindings = notif.Notification?.Visual?.Bindings;
        var textElements = bindings?.FirstOrDefault()?.GetTextElements();
        
        if (textElements != null && textElements.Count > 0)
        {
            string title = textElements[0].Text;
            string msg = textElements.Count > 1 ? string.Join(" ", textElements.Skip(1).Select(t => t.Text)) : "";
            string appName = notif.AppInfo?.DisplayInfo?.DisplayName ?? LocalizationService.Instance.NotificationFallbackAppName;
            string time = notif.CreationTime.ToString("HH:mm");

            Windows.Storage.Streams.RandomAccessStreamReference? logoStream = null;
            try
            {
                logoStream = notif.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(64, 64));
            } catch {}

            var item = new NotificationItem
            {
                Id = notif.Id,
                Title = title,
                Message = msg,
                AppName = appName,
                Time = time,
                AppLogoStream = logoStream
            };

            _dispatcherQueue.TryEnqueue(() =>
            {
                History.Insert(0, item);
                if (History.Count > 100) History.RemoveAt(History.Count - 1);
            });

            return item;
        }
        return null;
    }
}
