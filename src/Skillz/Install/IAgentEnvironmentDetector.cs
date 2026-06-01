using System.Collections.Immutable;

namespace Skillz.Install;

internal interface IAgentEnvironmentDetector
{
    Task<AgentDetectionResult> DetectAgentAsync(CancellationToken cancellationToken = default);

    Task<bool> IsRunningInAgentAsync(CancellationToken cancellationToken = default);

    Task<string?> GetAgentNameAsync(CancellationToken cancellationToken = default);

    string? GetAgentType(string agentName);

    Task<ImmutableArray<string>> DetectInstalledAgentsAsync(CancellationToken cancellationToken = default);
}

internal sealed record AgentDetectionResult(bool IsAgent, string? Name);
