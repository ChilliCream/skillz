using System.Collections.Immutable;

namespace Skillz.Install;

internal interface IAgentRegistry
{
    ImmutableDictionary<string, AgentConfig> All { get; }

    AgentConfig GetConfig(string agentType);

    bool TryGetConfig(string agentType, out AgentConfig? config);

    IReadOnlyList<string> ListAgentTypes();

    IReadOnlyList<string> GetUniversalAgents();

    IReadOnlyList<string> GetNonUniversalAgents();

    bool IsUniversalAgent(string agentType);
}
