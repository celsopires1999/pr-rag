namespace PrRag.Application.Services.Agents;

/// <summary>
/// Maps between a workflow's generated handoff tools and the capability each one
/// reaches.
/// </summary>
/// <remarks>
/// <para>
/// MAF names a handoff tool by the <em>position</em> of its target among the
/// participants (<c>handoff_to_1</c>, <c>handoff_to_2</c>) rather than by the
/// target's identity, so nothing on the composed agent says which capability a
/// given handoff reaches. The mapping is only recoverable from the order the
/// participants were added, which the application controls.
/// </para>
/// <para>
/// This is the single place that knows it. It is a positional convention owned by
/// the framework, so a change to MAF's naming is a change here too, and the
/// consequence is visible rather than silent: the report's handoff list stops
/// resolving and the tests that script a handoff by capability fail to compile.
/// Both are better outcomes than a handoff silently resolving to the wrong
/// capability in production telemetry.
/// </para>
/// </remarks>
public static class HandoffToolName
{
    private const string Prefix = "handoff_to_";

    /// <summary>
    /// The generated tool name for the participant at <paramref name="oneBasedIndex"/>
    /// in <paramref name="participantSlugs"/>.
    /// </summary>
    public static string ForParticipant(int oneBasedIndex)
        => $"{Prefix}{oneBasedIndex}";

    /// <summary>
    /// Resolves a generated handoff tool name to the capability slug it reaches,
    /// or null when the name is not a handoff tool or the position is not a
    /// participant in this graph.
    /// </summary>
    /// <remarks>
    /// A miss is a return value rather than a throw: this is called on every
    /// function call the model makes, most of which are ordinary tools. A
    /// recognised-looking name with an out-of-range position does throw, because
    /// that means the graph and the convention have diverged and any handoff
    /// recorded for the turn would be attributed to the wrong capability.
    /// </remarks>
    public static string? ResolveTarget(string? toolName, IReadOnlyList<string> participantSlugs)
    {
        if (toolName is null || !toolName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        if (!int.TryParse(toolName.AsSpan(Prefix.Length), out var oneBased) || oneBased < 1)
        {
            return null;
        }

        var index = oneBased - 1;
        if (index >= participantSlugs.Count)
        {
            throw new InvalidOperationException(
                $"The model called '{toolName}', but this graph has {participantSlugs.Count} "
                + $"participant(s) ({string.Join(", ", participantSlugs)}). The generated handoff names and "
                + "the participant order have diverged, so a handoff cannot be attributed to a capability. "
                + "Check HandoffToolName.");
        }

        return participantSlugs[index];
    }
}
