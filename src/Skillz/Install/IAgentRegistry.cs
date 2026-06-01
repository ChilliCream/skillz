using System.Collections.Immutable;

namespace Skillz.Install;

internal interface IAgentRegistry
{
    ImmutableDictionary<string, AgentConfig> All { get; }

    AgentConfig GetConfig(string agentType);

    bool TryGetConfig(string agentType, out AgentConfig? config);

    ImmutableArray<string> ListAgentTypes();

    ImmutableArray<string> GetUniversalAgents();

    ImmutableArray<string> GetNonUniversalAgents();

    bool IsUniversalAgent(string agentType);
}
