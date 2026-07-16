using System.Collections.ObjectModel;
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
        + "if that option exists, since this is the color space the Wii and N64 output. Also set Resolution/FPS "
        + "Type to Custom and Resolution to 720x480 - some capture card drivers (for example Elgato) otherwise "
        + "force their own color range conversion on top of OBS's, doubling any mismatch; a custom resolution "
        + "makes OBS take over the conversion completely.",
        "Mismatched color space settings distort colors before AutoLUT ever sees them. 720x480 is correct even "
        + "for the N64: NTSC signal timings are fixed, so capture cards digitize any NTSC source to 720x480 "
        + "regardless of the console's internal resolution.");

    private const string ScreenshotTail =
        "Screenshot the raw capture source: in OBS, right-click the source and use Screenshot (Source) with no "
        + "filters. Strongly recommended: bind a hotkey to Screenshot Selected Source (OBS Settings, Hotkeys) - "
        + "39 screenshots through the right-click menu is a good way to lose your mind.";

    private const string RequiredColorsNote =
        "All 9 gray colors (including black and white) are required; at least 20 of the 39 colors must be identified. ";

    private static readonly HelpStep PaletteScreenshotStep = new(
        "Screenshot all 39 colors",
        "Step through the colors with A (or use LEFT/RIGHT to go back/forth) and screenshot each one - 39 colors, any order, any filenames. "
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
        "Click Load images below - or drag and drop your screenshots anywhere onto this guide - then click Generate LUT in the bottom left.",
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

    // 0 = platform-select page, 1..Steps.Length = number of revealed steps.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlatformPage), nameof(NextLabel))]
    private int _stepIndex;

    private HelpStep[] _steps = [];
    private string _platform = "";

    /// <summary>Steps revealed so far - they stack up as the user clicks Next; Back hides the last one.</summary>
    public ObservableCollection<GuideStepItem> VisibleSteps { get; } = [];

    public bool IsPlatformPage => StepIndex == 0;

    public string NextLabel => StepIndex == _steps.Length ? "Done" : "Next";

    /// <summary>Opened via the main view model, which force-clears loaded images first.</summary>
    public void Open()
    {
        StepIndex = 0;
        VisibleSteps.Clear();
        IsOpen = true;
    }

    [RelayCommand]
    private void Back()
    {
        StepIndex--;
        if (VisibleSteps.Count > 0)
        {
            VisibleSteps.RemoveAt(VisibleSteps.Count - 1);
        }
    }

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
            RevealStep();
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
        VisibleSteps.Clear();
        StepIndex = 1;
        RevealStep();
    }

    private void RevealStep()
    {
        int index = VisibleSteps.Count + 1;
        var step = _steps[index - 1];
        VisibleSteps.Add(new GuideStepItem(index, _steps.Length, step.Title, step.Body, step.Note, _platform));
    }

    /// <summary>
    /// One revealed step in the guide; immutable, with the per-step button visibility
    /// precomputed from the step index and platform. The artifact buttons live on the
    /// get-colors step (index 2): the browser downloads directly (gz per version), the
    /// desktop copies the bundled artifact to the clipboard or opens its folder.
    /// </summary>
    public sealed class GuideStepItem
    {
        internal GuideStepItem(int index, int total, string title, string body, string? note, string platform)
        {
            Header = $"Step {index} of {total}: {title}";
            Body = body;
            Note = note;
            IsObsSetup = index == 1;

            bool isDownloadStep = index == 2;
            bool isBrowser = OperatingSystem.IsBrowser();
            ShowWiiDownload = isDownloadStep && isBrowser && platform == "wii";
            ShowN64Download = isDownloadStep && isBrowser && platform == "n64";
            ShowGzDownloads = isDownloadStep && isBrowser && platform == "gz";
            ShowWiiBundle = isDownloadStep && !isBrowser && platform == "wii";
            ShowN64Bundle = isDownloadStep && !isBrowser && platform == "n64";
            ShowGzBundle = isDownloadStep && !isBrowser && platform == "gz";
            ShowLoadImages = index == total;
        }

        public string Header { get; }
        public string Body { get; }
        public string? Note { get; }
        public bool HasNote => Note is not null;

        /// <summary>The OBS step keeps the amber "do this first" callout styling.</summary>
        public bool IsObsSetup { get; }

        public bool ShowWiiDownload { get; }
        public bool ShowN64Download { get; }
        public bool ShowGzDownloads { get; }
        public bool ShowWiiBundle { get; }
        public bool ShowN64Bundle { get; }
        public bool ShowGzBundle { get; }
        public bool ShowLoadImages { get; }
    }
}
