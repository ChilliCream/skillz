using System.CommandLine;
using Skillz.Interaction;

namespace Skillz.Commands;

internal sealed class InitCommand(IInteractionService interaction)
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

        if (File.Exists(skillFile))
        {
            interaction.WriteWarning($"Skill already exists at {displayPath}");
            return new CommandResult.Success();
        }

        if (hasName)
        {
            Directory.CreateDirectory(skillDir);
        }

        var content = BuildSkillTemplate(skillName);
        await File.WriteAllTextAsync(skillFile, content, cancellationToken);

        interaction.WriteSuccess($"Initialized skill: {skillName}");
        interaction.WriteLine();
        interaction.WriteDim("Created:");
        interaction.WriteLine($"  {displayPath}");
        interaction.WriteLine();
        interaction.WriteDim("Next steps:");
        interaction.WriteLine($"  1. Edit {displayPath} to define your skill instructions");
        interaction.WriteLine("  2. Update the name and description in the frontmatter");
        interaction.WriteLine();
        interaction.WriteDim("Publishing:");
        interaction.WriteLine("  GitHub: Push to a repo, then skillz add <owner>/<repo>");
        interaction.WriteLine($"  URL:    Host the file, then skillz add https://example.com/{displayPath}");

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
