using Ps2Ulix.Core;

namespace Ps2Ulix.Gui;

/// <param name="Input">Text box start value; null hides the box.</param>
/// <param name="Media">CD/DVD choice start value; null hides it.</param>
/// <param name="Details">Long read-only text (check/rebuild reports), shown monospaced.</param>
/// <param name="Choices">Options for a drop-down (e.g. target format); null hides it.</param>
/// <param name="InputMaxLength">Text box limit; 32 for UL names, longer for ISO file names.</param>
public sealed record PromptOptions(
    string Title,
    string Message,
    string? Input = null,
    UlMedia? Media = null,
    string? Details = null,
    string OkText = "OK",
    bool ShowCancel = true,
    IReadOnlyList<string>? Choices = null,
    int ChoiceIndex = 0,
    string ChoiceLabel = "Format:",
    int InputMaxLength = 32);

public sealed record PromptResult(string Input, UlMedia? Media, int Choice = 0);

/// <summary>UI the view model needs from the window; keeps the VM free of Avalonia controls.</summary>
public interface IDialogService
{
    Task<string?> PickIsoAsync();

    Task<string?> PickSaveIsoAsync(string suggestedName);

    Task<string?> PickFolderAsync();

    /// <returns>Null when cancelled.</returns>
    Task<PromptResult?> PromptAsync(PromptOptions options);

    /// <summary>Edits a game's OPL settings in place; false when cancelled.</summary>
    Task<bool> EditGameConfigAsync(string title, OplGameConfig config);
}
