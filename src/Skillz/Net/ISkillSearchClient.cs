namespace Skillz.Net;

internal interface ISkillSearchClient
{
    Task<IReadOnlyList<SearchSkill>> SearchAsync(string query, CancellationToken cancellationToken);
}
