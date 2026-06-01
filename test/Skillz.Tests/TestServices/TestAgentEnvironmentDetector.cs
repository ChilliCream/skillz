using System.Collections.Immutable;
using Skillz.Install;

namespace Skillz.Tests.TestServices;

internal sealed class TestAgentEnvironmentDetector : IAgentEnvironmentDetector
{
    public AgentDetectionResult DetectionResult { get; set; } = new(false, null);

    public string? AgentName { get; set; }

    public IReadOnlyList<string> InstalledAgents { get; set; } = Array.Empty<string>();

    public Dictionary<string, string?> AgentTypes { get; set; } = new(StringComparer.Ordinal);

    public Task<AgentDetectionResult> DetectAgentAsync()
    {
        return Task.FromResult(DetectionResult);
    }

    public Task<bool> IsRunningInAgentAsync()
    {
        return Task.FromResult(DetectionResult.IsAgent);
    }

    public Task<string?> GetAgentNameAsync()
    {
        return Task.FromResult(AgentName);
    }

    public string? GetAgentType(string agentName)
    {
        return AgentTypes.GetValueOrDefault(agentName);
    }

    public Task<ImmutableArray<string>> DetectInstalledAgentsAsync()
    {
        return Task.FromResult<ImmutableArray<string>>([.. InstalledAgents]);
    }
}
