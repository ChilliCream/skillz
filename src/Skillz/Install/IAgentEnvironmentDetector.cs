namespace Skillz.Install;

internal interface IAgentEnvironmentDetector
{
    Task<AgentDetectionResult> DetectAgentAsync();

    Task<bool> IsRunningInAgentAsync();

    Task<string?> GetAgentNameAsync();

    string? GetAgentType(string agentName);

    Task<IReadOnlyList<string>> DetectInstalledAgentsAsync();
}

internal sealed record AgentDetectionResult(bool IsAgent, string? Name);
