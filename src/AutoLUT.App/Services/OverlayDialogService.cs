using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoLUT.App.Services;

/// <summary>
/// Modal warning rendered as an overlay inside MainView. Used in the browser, where creating
/// dialog Windows throws (single-view lifetime); works on any platform.
/// </summary>
public partial class OverlayDialogService : ObservableObject, IDialogService
{
    private TaskCompletionSource? _dismissed;

    /// <summary>Completes when the last requested warning is dismissed. Warnings queue behind it:
    /// the overlay shows one at a time, and replacing an open one would strand its caller.</summary>
    private Task _queueTail = Task.CompletedTask;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _message = "";

    public Task ShowWarningAsync(string title, string message)
    {
        var shown = ShowAfterAsync(_queueTail, title, message);
        _queueTail = shown;
        return shown;
    }

    private async Task ShowAfterAsync(Task previous, string title, string message)
    {
        await previous;
        Title = title;
        Message = message;
        _dismissed = new TaskCompletionSource();
        IsOpen = true;
        await _dismissed.Task;
    }

    [RelayCommand]
    private void Dismiss()
    {
        IsOpen = false;
        _dismissed?.TrySetResult();
    }
}
