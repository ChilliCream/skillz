using System.Collections.Immutable;

namespace Skillz.Commands;

internal sealed record AddCommandOptions(
    string? Source,
    bool Global,
    ImmutableArray<string> Agents,
    ImmutableArray<string> SkillFilters,
    bool Yes,
    bool All,
    bool Copy,
    bool FullDepth,
    bool List);
