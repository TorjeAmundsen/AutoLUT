using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace AutoLUT.App.Services.Update;

public enum UpdateChoice { Update, Skip, Later, Never, Failed }

/// <summary>
/// The on-launch "update available" prompt. Code-built to match <see cref="MessageBox"/>'s
/// dark styling. On "Update" it swaps the buttons for an inline progress bar and runs the
/// supplied apply delegate (which exits the process on success); the other three buttons
/// just record the choice and close.
/// </summary>
public sealed class UpdateDialog : Window
{
    private readonly Func<IProgress<double>, Task> _apply;
    private readonly StackPanel _buttonPanel;
    private readonly StackPanel _progressPanel;
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _statusText;

    private UpdateChoice _result = UpdateChoice.Later;

    private UpdateDialog(UpdateInfo info, string currentVersion, Func<IProgress<double>, Task> apply)
    {
        _apply = apply;

        Title = "Update available";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#2C2C2C"));
        FontSize = 12;

        var headline = new TextBlock
        {
            Text = $"AutoLUT {info.Version} is available.",
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            Margin = new Thickness(20, 20, 20, 4),
        };

        var subtext = new TextBlock
        {
            Text = $"You have {currentVersion}.",
            Foreground = new SolidColorBrush(Color.Parse("#B0B0B0")),
            Margin = new Thickness(20, 0, 20, 12),
        };

        var notesLink = new Button
        {
            Content = "View release notes",
            Margin = new Thickness(20, 0, 20, 12),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#5C9EFF")),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        notesLink.Click += (_, _) =>
        {
            try
            {
                _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new Uri(info.ReleaseNotesUrl));
            }
            catch
            {
                // Opening a browser is best-effort.
            }
        };

        _buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(20, 4, 20, 16),
        };
        AddButton("Update", UpdateChoice.Update, isDefault: true);
        AddButton("Skip this version", UpdateChoice.Skip);
        AddButton("Remind me later", UpdateChoice.Later);
        AddButton("Don't ask again", UpdateChoice.Never);

        _statusText = new TextBlock { Text = "Downloading update..." };
        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Margin = new Thickness(0, 8, 0, 0),
            // The default indicator uses the same muted theme accent; use the app's blue.
            Foreground = new SolidColorBrush(Color.Parse("#3B7CC4")),
        };
        _progressPanel = new StackPanel
        {
            IsVisible = false,
            Margin = new Thickness(20, 4, 20, 16),
            Children = { _statusText, _progressBar },
        };

        Content = new StackPanel
        {
            Children = { headline, subtext, notesLink, _buttonPanel, _progressPanel },
        };
    }

    public static Task<UpdateChoice> Show(
        Window owner, UpdateInfo info, string currentVersion, Func<IProgress<double>, Task> apply)
    {
        var dialog = new UpdateDialog(info, currentVersion, apply);
        var tcs = new TaskCompletionSource<UpdateChoice>();
        dialog.Closed += (_, _) => tcs.TrySetResult(dialog._result);

        // Forward a synchronous ShowDialog failure to the TCS so the caller isn't left awaiting.
        _ = dialog.ShowDialog(owner).ContinueWith(
            t =>
            {
                if (t.IsFaulted && t.Exception is { } ex)
                {
                    tcs.TrySetException(ex.InnerExceptions);
                }
            },
            TaskScheduler.Default);

        return tcs.Task;
    }

    private void AddButton(string content, UpdateChoice choice, bool isDefault = false)
    {
        var button = new Button
        {
            Content = content,
            MinWidth = 72,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        if (isDefault)
        {
            // The Fluent "accent" class renders muted (near-disabled) on this dark window, so
            // style the primary action explicitly in the app's blue family instead. Border
            // matches the fill so the default gray outline doesn't frame the blue.
            button.Background = new SolidColorBrush(Color.Parse("#3B7CC4"));
            button.BorderBrush = new SolidColorBrush(Color.Parse("#3B7CC4"));
            button.Foreground = Brushes.White;
        }

        if (choice == UpdateChoice.Update)
        {
            button.Click += async (_, _) => await RunUpdateAsync();
        }
        else
        {
            button.Click += (_, _) =>
            {
                _result = choice;
                Close();
            };
        }

        _buttonPanel.Children.Add(button);
    }

    private async Task RunUpdateAsync()
    {
        _result = UpdateChoice.Update;
        _buttonPanel.IsVisible = false;
        _progressPanel.IsVisible = true;

        try
        {
            await _apply(new Progress<double>(p => _progressBar.Value = p));
            // Success exits the process inside _apply; nothing more runs here.
        }
        catch (Exception ex)
        {
            _result = UpdateChoice.Failed;
            _statusText.Text = $"Update failed: {ex.Message}";
            _progressBar.IsVisible = false;

            var close = new Button
            {
                Content = "Close",
                MinWidth = 72,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
            };
            close.Click += (_, _) => Close();
            _progressPanel.Children.Add(close);
        }
    }
}
