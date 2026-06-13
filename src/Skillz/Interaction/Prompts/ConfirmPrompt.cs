using Spectre.Console;

namespace Skillz.Interaction.Prompts;

/// <summary>
/// A yes/no prompt over Spectre's <see cref="ConfirmationPrompt"/>. Callers wrap this in a
/// <see cref="Decorators.WithDefault{T}"/> so it degrades to an explicit default (proceed for installs,
/// decline for the destructive removal confirm) when the console cannot show it - a redirected stream
/// or a non-ANSI terminal would otherwise hang or throw.
/// </summary>
internal sealed class ConfirmPrompt : IPrompt<bool>
{
    private readonly ConfirmationPrompt _prompt;

    public ConfirmPrompt(string message, bool defaultValue)
    {
        _prompt = new ConfirmationPrompt(Markup.Escape(message)) { DefaultValue = defaultValue };
    }

    public Task<bool> ShowAsync(IAnsiConsole console, CancellationToken cancellationToken)
        => console.PromptAsync(_prompt, cancellationToken);
}
