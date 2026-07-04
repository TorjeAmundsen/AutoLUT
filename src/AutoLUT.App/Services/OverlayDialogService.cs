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

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _message = "";

    public Task ShowWarningAsync(string title, string message)
    {
        Title = title;
        Message = message;
        IsOpen = true;
        _dismissed = new TaskCompletionSource();
        return _dismissed.Task;
    }

    [RelayCommand]
    private void Dismiss()
    {
        IsOpen = false;
        _dismissed?.TrySetResult();
    }
}
