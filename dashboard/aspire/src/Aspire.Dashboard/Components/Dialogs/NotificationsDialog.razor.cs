// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Dashboard.Model;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Aspire.Dashboard.Components.Dialogs;

public partial class NotificationsDialog : IDisposable
{
    private IReadOnlyList<NotificationMessage> _notifications = [];
    private DashboardContent? _viewerContent;
    private string? _viewerTitle;

    [Inject]
    public required Aspire.Dashboard.Model.INotificationService NotificationService { get; init; }

    [CascadingParameter]
    public IDialogInstance Dialog { get; set; } = default!;

    protected override void OnInitialized()
    {
        _notifications = NotificationService.GetNotifications();
        NotificationService.OnChange += HandleNotificationsChanged;
        NotificationService.ResetUnreadCount();
    }

    private void HandleNotificationsChanged()
    {
        _ = InvokeAsync(() =>
        {
            _notifications = NotificationService.GetNotifications();
            StateHasChanged();
        });
    }

    private void DismissAll()
    {
        NotificationService.ClearAll();
    }

    private void Dismiss(string id)
    {
        NotificationService.RemoveNotification(id);
    }

    private void OpenViewer(NotificationEntry entry)
    {
        if (entry.Content is { } content)
        {
            _viewerContent = content;
            _viewerTitle = entry.Title;
        }
    }

    private void CloseViewer()
    {
        _viewerContent = null;
        _viewerTitle = null;
    }

    public void Dispose()
    {
        NotificationService.OnChange -= HandleNotificationsChanged;
    }
}
