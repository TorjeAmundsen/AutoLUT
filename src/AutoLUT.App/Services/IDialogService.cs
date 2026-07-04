using Avalonia.Controls;

namespace AutoLUT.App.Services;

public interface IDialogService
{
    Task ShowWarningAsync(string title, string message);
}

public sealed class DialogService : IDialogService
{
    private readonly Window _owner;

    public DialogService(Window owner) => _owner = owner;

    public Task ShowWarningAsync(string title, string message) => MessageBox.Show(_owner, title, message);
}
