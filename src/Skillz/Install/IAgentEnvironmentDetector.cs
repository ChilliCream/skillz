using System.Collections.Immutable;

namespace Skillz.Install;

internal interface IAgentEnvironmentDetector
{
    AgentDetectionResult DetectAgent { get; }

    string? GetAgentType(string agentName);

    ImmutableArray<string> DetectInstalledAgents();
}

internal sealed record AgentDetectionResult(bool IsAgent, string? Name);
