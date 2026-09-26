namespace PrRag.Application.Services.Agents;

/// <summary>
/// Owns the skill-related state of a <see cref="AgentSessionState"/>: activation,
/// per-turn guidance injection, clearing once a workflow completes, and
/// reporting. Keeps the state keys out of the chat orchestrator and the tools.
///
/// <para>
/// The guidance body is injected as a message on the turn *after* activation,
/// because the model must first call <c>activate_skill</c> and see what it
/// returned; re-injecting it on later turns would restate it forever.
/// </para>
/// </summary>
public static class SkillSessionState
{
    /// <summary>Records an activated skill, arming its guidance for the next turn.</summary>
    public static void Activate(AgentSessionState state, string name, string body)
    {
        state.SkillId = name;
        state.SkillBody = body;
        state.SkillBodyInjected = false;
    }

    /// <summary>
    /// Returns the guidance body of the active skill when it has not yet been
    /// injected, otherwise null.
    /// </summary>
    public static string? ReadActiveSkillBody(AgentSessionState state)
    {
        if (string.IsNullOrWhiteSpace(state.SkillId) || state.SkillBodyInjected)
        {
            return null;
        }

        return state.SkillBody;
    }

    /// <summary>Marks the active skill guidance as injected.</summary>
    public static void MarkInjected(AgentSessionState state) => state.SkillBodyInjected = true;

    /// <summary>Drops all skill state.</summary>
    public static void Clear(AgentSessionState state)
    {
        state.SkillId = null;
        state.SkillBody = null;
        state.SkillBodyInjected = false;
    }

    public static (string? SkillName, bool Active) DescribeForReport(AgentSessionState? state)
    {
        if (state is null || string.IsNullOrWhiteSpace(state.SkillId))
        {
            return (null, false);
        }

        return (state.SkillId, true);
    }
}
