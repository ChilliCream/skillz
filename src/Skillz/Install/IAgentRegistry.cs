using System.Collections.Immutable;

namespace Skillz.Install;

internal interface IAgentRegistry
{
    ImmutableDictionary<string, AgentConfig> All { get; }

    ImmutableArray<string> AgentTypes { get; }

    ImmutableArray<string> UniversalAgents { get; }

    ImmutableArray<string> NonUniversalAgents { get; }

    AgentConfig GetConfig(string agentType);

    bool TryGetConfig(string agentType, out AgentConfig? config);

    bool IsUniversalAgent(string agentType);
}
