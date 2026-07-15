using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoLUT.App.ViewModels;

/// <summary>State and content for the step-through "How to use" wizard overlay.</summary>
public partial class HelpWizardViewModel : ObservableObject
{
    private sealed record HelpStep(string Title, string Body, string? Note = null);

    private static readonly HelpStep ObsStep = new(
        "OBS setup - do this first",
        "In Settings, Advanced, Video set Color Space to Rec. 709 and Color Range to Limited, since this is "
        + "what modern streaming sites expect. In your capture source's Properties, set Color Space to Rec. 601 "
        + "if that option exists, since this is the color space the Wii and N64 output.",
        "Mismatched color space settings distort colors before AutoLUT ever sees them.");

    private const string ScreenshotTail =
        "Screenshot the raw capture source: in OBS, right-click the source and use Screenshot (Source) with no "
        + "filters. Strongly recommended: bind a hotkey to Screenshot Selected Source (OBS Settings, Hotkeys) - "
        + "39 screenshots through the right-click menu is a good way to lose your mind.";

    private const string RequiredColorsNote =
        "All 9 gray colors (including black and white) are required; at least 20 of the 39 colors must be identified. ";

    private static readonly HelpStep PaletteScreenshotStep = new(
        "Screenshot all 39 colors",
        "Step through the colors with LEFT/RIGHT and screenshot each one - 39 colors, any order, any filenames. "
        + ScreenshotTail,
        RequiredColorsNote + "The palette app's corner label in the screenshots is fine - just keep the center of the screen clear.");

    private static readonly HelpStep GzScreenshotStep = new(
        "Screenshot all 39 colors",
        "Load each savestate and screenshot it - 39 colors, any order, any filenames. " + ScreenshotTail,
        RequiredColorsNote + "The game HUD in the screenshots is fine - just keep the center of the screen clear.");

    // The guide auto-closes when Generate succeeds, so everything after generation
    // (check the preview, save, apply in OBS) lives in this final step's note.
    private static readonly HelpStep LoadStep = new(
        "Load and generate",
        "Click Load images below - or drag and drop your screenshots anywhere onto this guide - then click Generate LUT.",
        "When generation finishes, this guide closes and the corrected preview appears - it matches exactly what OBS "
        + "will render. Then Save LUT.png and in OBS: right-click your capture source, Filters, add Apply LUT, and select the file.");

    // The get-colors step differs per platform: web offers the download right in the wizard,
    // desktop bundles the savestates and points at the GitHub releases page for the rest.

    private static readonly HelpStep[] WiiSteps =
    [
        ObsStep,
        new("Get the calibration colors onto your console", OperatingSystem.IsBrowser()
            ? "Use the download button below to get the app, extract the zip to the root of your SD card, then launch it from the Homebrew Channel."
            : "Use Copy app to clipboard below and paste it into the root of your SD card - it pastes as an apps folder "
              + "that merges with the apps folder already on your card. Then launch it from the Homebrew Channel."),
        PaletteScreenshotStep,
        LoadStep,
    ];

    private static readonly HelpStep[] N64Steps =
    [
        ObsStep,
        new("Get the calibration colors onto your console", OperatingSystem.IsBrowser()
            ? "Use the download button below to get the ROM, put it on your flashcart's SD card and boot it."
            : "Use Copy ROM to clipboard below and paste it wherever you see fit on your flashcart's SD card, then boot it."),
        PaletteScreenshotStep,
        LoadStep,
    ];

    private static readonly HelpStep[] GzSteps =
    [
        ObsStep,
        new("Get the calibration colors onto your console", OperatingSystem.IsBrowser()
            ? "Use the download button matching your game version (1.0 or 1.2) below and copy the folder to your SD card."
            : "Use the copy button matching your game version (1.0 or 1.2) below and paste the folder wherever "
              + "you see fit on your SD card, then load the states with gz."),
        GzScreenshotStep,
        LoadStep,
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    private bool _isOpen;

    /// <summary>Label for the bottom-bar button that toggles the guide.</summary>
    public string ToggleLabel => IsOpen ? "Close guide" : "Open guide";

    // 0 = platform-select page, 1..Steps.Length = wizard steps.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlatformPage), nameof(IsObsSetupStep), nameof(StepHeader),
        nameof(StepBody), nameof(StepNote), nameof(HasStepNote), nameof(NextLabel),
        nameof(ShowWiiDownload), nameof(ShowN64Download), nameof(ShowGzDownloads),
        nameof(ShowWiiBundle), nameof(ShowN64Bundle), nameof(ShowGzBundle), nameof(ShowLoadImages))]
    private int _stepIndex;

    private HelpStep[] _steps = [];
    private string _platform = "";

    public bool IsPlatformPage => StepIndex == 0;

    /// <summary>The OBS step keeps the amber "do this first" callout styling.</summary>
    public bool IsObsSetupStep => StepIndex == 1;

    // Artifact buttons live on the get-colors step (index 2). In the browser they download
    // directly (gz per version); on desktop the artifacts are bundled next to the program,
    // so each platform gets a copy-to-clipboard button and an open-folder button.
    private bool IsDownloadStep => StepIndex == 2;
    private bool IsBundleStep => IsDownloadStep && !OperatingSystem.IsBrowser();
    public bool ShowWiiDownload => IsDownloadStep && _platform == "wii" && OperatingSystem.IsBrowser();
    public bool ShowN64Download => IsDownloadStep && _platform == "n64" && OperatingSystem.IsBrowser();
    public bool ShowGzDownloads => IsDownloadStep && _platform == "gz" && OperatingSystem.IsBrowser();
    public bool ShowWiiBundle => IsBundleStep && _platform == "wii";
    public bool ShowN64Bundle => IsBundleStep && _platform == "n64";
    public bool ShowGzBundle => IsBundleStep && _platform == "gz";

    /// <summary>The final step gets its own Load images button so the guide is actionable in place.</summary>
    public bool ShowLoadImages => StepIndex > 0 && StepIndex == _steps.Length;

    public string StepHeader => IsPlatformPage ? "" : $"Step {StepIndex} of {_steps.Length}: {_steps[StepIndex - 1].Title}";
    public string StepBody => IsPlatformPage ? "" : _steps[StepIndex - 1].Body;
    public string? StepNote => IsPlatformPage ? null : _steps[StepIndex - 1].Note;
    public bool HasStepNote => StepNote is not null;
    public string NextLabel => StepIndex == _steps.Length ? "Done" : "Next";

    /// <summary>Opened via the main view model, which force-clears loaded images first.</summary>
    public void Open()
    {
        StepIndex = 0;
        IsOpen = true;
    }

    [RelayCommand]
    private void Back() => StepIndex--;

    [RelayCommand]
    private void Next()
    {
        if (StepIndex == _steps.Length)
        {
            IsOpen = false;
        }
        else
        {
            StepIndex++;
        }
    }

    [RelayCommand]
    private void SelectPlatform(string platform)
    {
        _platform = platform;
        _steps = platform switch
        {
            "wii" => WiiSteps,
            "n64" => N64Steps,
            _ => GzSteps,
        };
        StepIndex = 1; // StepIndex change notifications cover every derived property
    }
}
