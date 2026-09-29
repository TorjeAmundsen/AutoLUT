using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoLUT.App.ViewModels;

/// <summary>State and content for the step-through "How to use" guide.</summary>
public partial class HelpWizardViewModel : ObservableObject
{
    /// <summary>The buttons a step shows under its body text.</summary>
    internal enum StepAction
    {
        None,
        GetColors,
        LoadImages,
    }

    internal sealed record HelpStep(
        string Title,
        string Body,
        string? Note = null,
        StepAction Action = StepAction.None,
        bool IsObsSetup = false,
        IReadOnlyList<SettingsGroup>? Settings = null,
        IReadOnlyList<string>? Items = null,
        string? BodyAfterItems = null);

    /// <summary>One OBS dialog and the settings to change in it.</summary>
    public sealed record SettingsGroup(string Location, IReadOnlyList<SettingRow> Rows, string? Note = null);

    public sealed record SettingRow(string Name, string Value, string? Note = null);

    public sealed record NumberedItem(string Number, string Text);

    // The source's Color Range row differs per platform: the palette apps have a setup check step for it,
    // gz users only get AutoLUT's warning after generating.
    private static HelpStep ObsStep(string colorRangeNote) => new(
        "OBS setup - do this first",
        "Incorrect OBS settings WILL force you to re-take all captures if you want optimal and accurate "
        + "results, so follow these instructions closely.",
        "Mismatched color space settings distort colors before AutoLUT ever sees them.",
        IsObsSetup: true,
        Settings:
        [
            new("OBS Settings → Advanced → Video",
                [new("Color Space", "Rec. 709"), new("Color Range", "Limited")],
                "This is what modern streaming sites expect."),
            new("Your capture source's Properties",
            [
                new("Color Space", "Rec. 601",
                    "If that option exists. This is the color space the Wii and N64 output."),
                new("Resolution/FPS Type", "Custom"),
                new("Resolution", "720x480",
                    "Some capture card drivers (for example Elgato) otherwise force their own color range conversion "
                    + "on top of OBS's, doubling any mismatch; a custom resolution makes OBS take over the conversion "
                    + "completely. 720x480 is correct even for the N64: NTSC signal timings are fixed, so capture "
                    + "cards digitize any NTSC source to 720x480 regardless of the console's internal resolution."),
                new("Color Range", "Leave as it is for now", colorRangeNote),
            ]),
        ]);

    private const string PaletteColorRangeNote =
        "The \"Check your color range\" step walks you through the app's check screen, which tells you whether to "
        + "change it.";

    private const string GzColorRangeNote =
        "If it's wrong, AutoLUT warns about washed-out or crushed colors when you generate and tells you which way "
        + "to set it.";

    private static readonly HelpStep CropScaleStep = new(
        "Crop and scale your game (optional)",
        "Not relevant to AutoLUT itself, but recommended regardless: to crop and scale a 4:3 game optimally, "
        + "never use OBS' transform features (drag to scale, alt-drag to crop) - use filters for everything, "
        + "ordered:",
        Items: ["Apply LUT", "Crop/Pad", "Scaling/Aspect Ratio"],
        BodyAfterItems: "Set Crop/Pad per game with the game running, since games render at different resolutions "
        + "(basically none use 640x480 or 320x240).\n\n"
        + "Set Scaling/Aspect Ratio to the 4:3 resolution that fills your canvas vertically - 1440x1080 on a "
        + "1920x1080 canvas - not just '4:3', with scale filtering on Area. Point also works for a really "
        + "pixelated/harsh look, at the cost of uneven pixel row/column widths; never use the other scale "
        + "filtering options here.");

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

    // Palette apps only: they open on a setup check screen, toggled with B.
    private static readonly HelpStep SetupCheckStep = new(
        "Check your color range",
        "The app opens on a setup check screen. The top half is black with four dark boxes (8, 16, 24, 32). "
        + "The bottom half is white with four light boxes (247, 239, 231, 223). Look at it in the OBS preview, "
        + "with the Apply LUT filter off if you already have one:",
        Items:
        [
            "All 8 boxes visible, black half black, white half white: your color range is right.",
            "Boxes 8 and 16 (or 247 and 239) blend into the background: the capture is crushed. Set Color Range "
            + "in the capture source's Properties to Full.",
            "Every box is visible, but the black half looks dark gray next to OBS's black canvas and the white "
            + "half looks dim: the capture is washed out. Set Color Range to Partial.",
        ],
        Note: "Not sure? Try each Color Range option and keep the one with the darkest black and brightest white where all "
        + "8 boxes still show. If boxes vanish on every option, recheck the custom 720x480 resolution from step 1. "
        + "Press B to go to the colors. B brings the check screen back at any time and returns you to the same "
        + "color. Don't include the check screen in your calibration screenshots.");

    private static readonly HelpStep GzScreenshotStep = new(
        "Screenshot all 39 colors",
        "Load each savestate and screenshot it - 39 colors, any order, any filenames. " + ScreenshotTail,
        RequiredColorsNote + "The game HUD in the screenshots is fine - just keep the center of the screen clear.");

    private static readonly HelpStep LoadStep = new(
        "Load and generate",
        "Click Load images below - or drag and drop your screenshots anywhere onto this guide - then click Generate LUT in the bottom left.",
        Action: StepAction.LoadImages);

    private static readonly HelpStep SaveStep = new(
        "Save and apply in OBS",
        "Close this guide to check the corrected preview - it matches exactly what OBS will render. Then click Save "
        + "LUT.png in the bottom left, and in OBS right-click your capture source, Filters, add Apply LUT, and select "
        + "the file.",
        "Show Corrected Image in the bottom left switches the preview between the raw capture and the corrected one.");

    // The get-colors step differs per platform: web offers the download right in the guide,
    // desktop bundles the savestates and points at the GitHub releases page for the rest.

    private static readonly HelpStep[] WiiSteps =
    [
        ObsStep(PaletteColorRangeNote),
        new("Get the calibration colors onto your console", OperatingSystem.IsBrowser()
            ? "Use the download button below to get the app, extract the zip to the root of your SD card, then launch it from the Homebrew Channel."
            : "Use Copy app to clipboard below and paste it into the root of your SD card - it pastes as an apps folder "
              + "that merges with the apps folder already on your card. Then launch it from the Homebrew Channel.",
            Action: StepAction.GetColors),
        SetupCheckStep,
        PaletteScreenshotStep,
        LoadStep,
        SaveStep,
        CropScaleStep,
    ];

    private static readonly HelpStep[] N64Steps =
    [
        ObsStep(PaletteColorRangeNote),
        new("Get the calibration colors onto your console", OperatingSystem.IsBrowser()
            ? "Use the download button below to get the ROM, put it on your flashcart's SD card and boot it."
            : "Use Copy ROM to clipboard below and paste it wherever you see fit on your flashcart's SD card, then boot it.",
            Action: StepAction.GetColors),
        SetupCheckStep,
        PaletteScreenshotStep,
        LoadStep,
        SaveStep,
        CropScaleStep,
    ];

    private static readonly HelpStep[] GzSteps =
    [
        ObsStep(GzColorRangeNote),
        new("Get the calibration colors onto your console", OperatingSystem.IsBrowser()
            ? "Use the download button matching your game version (1.0 or 1.2) below and copy the folder to your SD card."
            : "Use the copy button matching your game version (1.0 or 1.2) below and paste the folder wherever "
              + "you see fit on your SD card, then load the states with gz.",
            Action: StepAction.GetColors),
        GzScreenshotStep,
        LoadStep,
        SaveStep,
        CropScaleStep,
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    private bool _isOpen;

    /// <summary>Label for the bottom-bar button that toggles the guide.</summary>
    public string ToggleLabel => IsOpen ? "Close guide" : "Open guide";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextLabel))]
    private GuideStepItem[] _steps = [];

    /// <summary>Null while the platform choice page shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlatformPage), nameof(NextLabel))]
    private GuideStepItem? _currentStep;

    public bool IsPlatformPage => CurrentStep is null;

    public string NextLabel => CurrentStep?.Number == Steps.Length ? "Done" : "Next";

    [RelayCommand]
    private void Back() =>
        CurrentStep = CurrentStep is { Number: > 1 } step ? Steps[step.Number - 2] : null;

    [RelayCommand]
    private void Next()
    {
        if (CurrentStep is not { } step)
        {
            return;
        }

        if (step.Number == Steps.Length)
        {
            IsOpen = false;
        }
        else
        {
            CurrentStep = Steps[step.Number];
        }
    }

    [RelayCommand]
    private void SelectPlatform(string platform)
    {
        var steps = platform switch
        {
            "wii" => WiiSteps,
            "n64" => N64Steps,
            _ => GzSteps,
        };
        Steps = steps.Select((step, i) => new GuideStepItem(i + 1, steps.Length, step, platform)).ToArray();
        CurrentStep = Steps[0];
    }

    /// <summary>Called after a successful generate, so the guide moves on without the user clicking Next.</summary>
    public void MovePastGenerateStep()
    {
        int index = Array.FindIndex(Steps, step => step.ShowLoadImages);
        if (index >= 0 && index + 1 < Steps.Length)
        {
            CurrentStep = Steps[index + 1];
        }
    }

    /// <summary>
    /// One step in the guide; immutable, with the button visibility precomputed from the
    /// step's action and platform. The browser downloads directly (gz per version), the
    /// desktop copies the bundled artifact to the clipboard or opens its folder.
    /// </summary>
    public sealed class GuideStepItem
    {
        internal GuideStepItem(int number, int total, HelpStep step, string platform)
        {
            Number = number;
            Title = step.Title;
            Header = $"Step {number} of {total}: {step.Title}";
            Body = step.Body;
            Note = step.Note;
            Settings = step.Settings;
            Items = step.Items?.Select((text, i) => new NumberedItem($"{i + 1}.", text)).ToArray();
            BodyAfterItems = step.BodyAfterItems;
            IsObsSetup = step.IsObsSetup;

            bool isGetColors = step.Action == StepAction.GetColors;
            bool isBrowser = OperatingSystem.IsBrowser();
            ShowWiiDownload = isGetColors && isBrowser && platform == "wii";
            ShowN64Download = isGetColors && isBrowser && platform == "n64";
            ShowGzDownloads = isGetColors && isBrowser && platform == "gz";
            ShowWiiBundle = isGetColors && !isBrowser && platform == "wii";
            ShowN64Bundle = isGetColors && !isBrowser && platform == "n64";
            ShowGzBundle = isGetColors && !isBrowser && platform == "gz";
            ShowLoadImages = step.Action == StepAction.LoadImages;
        }

        public int Number { get; }
        public string Title { get; }
        public string Header { get; }
        public string Body { get; }
        public string? Note { get; }
        public IReadOnlyList<SettingsGroup>? Settings { get; }
        public IReadOnlyList<NumberedItem>? Items { get; }
        public string? BodyAfterItems { get; }

        /// <summary>The OBS step's note is part of its instructions, so it shows as normal text; other steps' notes are subtext.</summary>
        public bool HasPrimaryNote => Note is not null && IsObsSetup;

        public bool HasSubtleNote => Note is not null && !IsObsSetup;

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
