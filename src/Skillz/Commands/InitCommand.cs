using System.CommandLine;
using Skillz.Install;
using Skillz.Interaction;
using Skillz.Plugins;
using Skillz.Skills;
using Skillz.Utils;
using Spectre.Console;
using static Skillz.KnownConfigNames;

namespace Skillz.Commands;

internal sealed class InitCommand(
    IInteractionService interaction,
    IFileStore fileStore,
    ISystemEnvironment systemEnvironment)
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

        var cwd = systemEnvironment.CurrentDirectory;
        var hasName = !string.IsNullOrEmpty(nameArg);

        // Sanitize the raw argument into a safe single path component so a value
        // like "../escape" or "/etc/foo" cannot redirect the output outside cwd.
        var skillName = hasName ? SkillNameSanitizer.SanitizeName(nameArg!) : Path.GetFileName(cwd);
        if (string.IsNullOrEmpty(skillName))
        {
            skillName = "skill";
        }

        var skillDir = hasName ? Path.Combine(cwd, skillName) : cwd;

        // Defense-in-depth: the sanitizer already neutralizes traversal, but
        // verify the resolved directory stays within cwd before writing.
        if (hasName && !PathContainment.IsContainedInRealPath(skillDir, cwd))
        {
            throw new CliException(
                ExitCodeConstants.Failure,
                $"Invalid skill name '{nameArg}': the target directory would be outside the current directory.");
        }

        var skillFile = Path.Combine(skillDir, SkillFileName);
        var displayPath = hasName ? $"{skillName}/{SkillFileName}" : SkillFileName;

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
