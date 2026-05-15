namespace Skillz.Commands;

internal sealed record AddCommandOptions(
    string? Source,
    bool Global,
    IReadOnlyList<string> Agents,
    IReadOnlyList<string> SkillFilters,
    bool Yes,
    bool All,
    bool Copy,
    bool FullDepth,
    bool List);
