using Ps2Ulix.Core;

namespace Ps2Ulix.Gui.Tests;

/// <summary>Scripted dialogs: prompts return queued answers (null = cancel); config edits run <see cref="EditConfig"/>.</summary>
internal sealed class FakeDialogs : IDialogService
{
    public Queue<PromptResult?> Answers { get; } = new();
    public List<PromptOptions> Prompts { get; } = [];

    /// <summary>Applied to the config on "Save"; null means the user cancels.</summary>
    public Action<OplGameConfig>? EditConfig { get; set; }

    public Task<string?> PickIsoAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickSaveIsoAsync(string suggestedName) => Task.FromResult<string?>(null);
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);

    public Task<PromptResult?> PromptAsync(PromptOptions options)
    {
        Prompts.Add(options);
        return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : null);
    }

    public Task<bool> EditGameConfigAsync(string title, OplGameConfig config)
    {
        EditConfig?.Invoke(config);
        return Task.FromResult(EditConfig is not null);
    }
}
