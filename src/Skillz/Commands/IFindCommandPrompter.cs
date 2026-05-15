using Skillz.Net;

namespace Skillz.Commands;

internal interface IFindCommandPrompter
{
    Task<SearchSkill?> RunInteractiveSearchAsync(string initialQuery, CancellationToken cancellationToken);
}
