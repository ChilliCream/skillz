using System.CommandLine;
using Skillz.Interaction;
using Skillz.Utils;
using Spectre.Console;

namespace Skillz.Commands;

internal sealed class InitCommand(IInteractionService interaction, IFileStore fileStore)
    : BaseCommand("init", "Initialize a new skill (creates SKILL.md)")
{
    private readonly Argument<string?> _nameArgument = new("name")
    {
        Description = "Skill name (creates <name>/SKILL.md). Defaults to current directory.",
        Arity = ArgumentArity.ZeroOrOne
    };

    protected override void Configure()
    {
        Arguments.Add(_nameArgument);
    }

    protected override async Task<CommandResult> ExecuteAsync(
        ParseResult parseResult,
        CancellationToken cancellationToken)
    {
        var nameArg = parseResult.GetValue(_nameArgument);

        var cwd = Directory.GetCurrentDirectory();
        var hasName = !string.IsNullOrEmpty(nameArg);
        var skillName = hasName ? nameArg! : Path.GetFileName(cwd);
        if (string.IsNullOrEmpty(skillName))
        {
            skillName = "skill";
        }

        var skillDir = hasName ? Path.Combine(cwd, skillName) : cwd;
        var skillFile = Path.Combine(skillDir, KnownConfigNames.SkillFileName);
        var displayPath = hasName ? $"{skillName}/{KnownConfigNames.SkillFileName}" : KnownConfigNames.SkillFileName;

        if (fileStore.FileExists(skillFile))
        {
            interaction.WriteWarning($"Skill already exists at {displayPath}");
            return new CommandResult.Success();
        }

        if (hasName)
        {
            fileStore.CreateDirectory(skillDir);
        }

        var content = BuildSkillTemplate(skillName);
        await fileStore.WriteAllTextAsync(skillFile, content, cancellationToken);

        interaction.WriteMarkupLine(
            $"""
            [green]Initialized skill: {Markup.Escape(skillName)}[/]

            [dim]Created:[/]
              {Markup.Escape(displayPath)}

            [dim]Next steps:[/]
              1. Edit {Markup.Escape(displayPath)} to define your skill instructions
              2. Update the name and description in the frontmatter

            [dim]Publishing:[/]
              GitHub: Push to a repo, then skillz add <owner>/<repo>
              URL:    Host the file, then skillz add https://example.com/{Markup.Escape(displayPath)}
            """);

        return new CommandResult.Success();
    }

    private static string BuildSkillTemplate(string skillName)
    {
        return $"""
            ---
            name: {skillName}
            description: A brief description of what this skill does
            ---

            # {skillName}

            Instructions for the agent to follow when this skill is activated.

            ## When to use

            Describe when this skill should be used.

            ## Instructions

            1. First step
            2. Second step
            3. Additional steps as needed
            """;
    }
}
