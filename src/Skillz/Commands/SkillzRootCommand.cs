using System.CommandLine;

namespace Skillz.Commands;

internal sealed class SkillzRootCommand : RootCommand
{
    public SkillzRootCommand(
        AddCommand add,
        RemoveCommand remove,
        ListCommand list,
        InitCommand init,
        UpdateCommand update) : base("Skillz - AI agent skill manager")
    {
        Subcommands.Add(add);
        Subcommands.Add(remove);
        Subcommands.Add(list);
        Subcommands.Add(init);
        Subcommands.Add(update);
    }
}
