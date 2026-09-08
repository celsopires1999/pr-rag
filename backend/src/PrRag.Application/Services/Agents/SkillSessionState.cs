using Microsoft.Agents.AI;

namespace PrRag.Application.Services.Agents;

/// <summary>
/// Owns the skill-related keys and logic of the <see cref="AgentSession"/> state
/// bag: activation, per-turn guidance injection, clearing once a workflow
/// completes, and reporting. Keeps the state-bag keys out of the chat
/// orchestrator and the tools.
/// </summary>
public static class SkillSessionState
{
    private const string SkillIdKey = "SkillId";
    private const string SkillBodyKey = "SkillBody";
    private const string SkillBodyInjectedKey = "SkillBodyInjected";

    /// <summary>Records an activated skill in the session state bag.</summary>
    public static void Activate(AgentSession session, string name, string body)
    {
        session.StateBag.SetValue(SkillIdKey, name);
        session.StateBag.SetValue(SkillBodyKey, body);
        session.StateBag.TryRemoveValue(SkillBodyInjectedKey);
    }

    /// <summary>
    /// Returns the guidance body of the active skill when it has not yet been
    /// injected on this session, otherwise null.
    /// </summary>
    public static string? ReadActiveSkillBody(AgentSession session)
    {
        if (!session.StateBag.TryGetValue<string>(SkillIdKey, out var skillId)
            || string.IsNullOrWhiteSpace(skillId))
        {
            return null;
        }

        // Guidance already injected in an earlier turn of this session.
        if (session.StateBag.TryGetValue<string>(SkillBodyInjectedKey, out var injected) && injected == "true")
        {
            return null;
        }

        return session.StateBag.TryGetValue<string>(SkillBodyKey, out var body) ? body : null;
    }

    /// <summary>Marks the active skill guidance as injected for this session.</summary>
    public static void MarkInjected(AgentSession session)
    {
        session.StateBag.SetValue(SkillBodyInjectedKey, "true");
    }

    /// <summary>Drops all skill state from the session state bag.</summary>
    public static void Clear(AgentSession session)
    {
        session.StateBag.TryRemoveValue(SkillIdKey);
        session.StateBag.TryRemoveValue(SkillBodyKey);
        session.StateBag.TryRemoveValue(SkillBodyInjectedKey);
    }

    public static (string? SkillName, bool Active) DescribeForReport(AgentSession? session)
    {
        if (session is null)
        {
            return (null, false);
        }

        if (!session.StateBag.TryGetValue<string>(SkillIdKey, out var skillId) || string.IsNullOrWhiteSpace(skillId))
        {
            return (null, false);
        }

        return (skillId, true);
    }
}