using System.Collections.Immutable;
using Skillz.Install;

namespace Skillz.Tests.TestServices;

internal sealed class TestAgentEnvironmentDetector : IAgentEnvironmentDetector
{
    public AgentDetectionResult DetectAgent { get; set; } = new(false, null);

    public IReadOnlyList<string> InstalledAgents { get; set; } = Array.Empty<string>();

    public Dictionary<string, string?> AgentTypes { get; set; } = new(StringComparer.Ordinal);

    public string? GetAgentType(string agentName)
    {
        return AgentTypes.GetValueOrDefault(agentName);
    }

    public ImmutableArray<string> DetectInstalledAgents()
    {
        return [.. InstalledAgents];
    }
}
