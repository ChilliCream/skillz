using System.Collections.Immutable;

namespace Skillz.Install;

internal interface IAgentEnvironmentDetector
{
    Task<AgentDetectionResult> DetectAgentAsync(CancellationToken cancellationToken);

    Task<bool> IsRunningInAgentAsync(CancellationToken cancellationToken);

    Task<string?> GetAgentNameAsync(CancellationToken cancellationToken);

    string? GetAgentType(string agentName);

    Task<ImmutableArray<string>> DetectInstalledAgentsAsync(CancellationToken cancellationToken);
}

internal sealed record AgentDetectionResult(bool IsAgent, string? Name);
