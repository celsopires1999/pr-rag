using Microsoft.Extensions.DependencyInjection;
using PrRag.Application.Services.Agents;
using PrRag.Application.Services.Agents.Specialists;

namespace PrRag.Tests;

/// <summary>
/// Scripts which specialist a turn hands off to.
/// </summary>
/// <remarks>
/// MAF names the generated handoff tool by position among participants rather than
/// by target, so a test that wants "hand this turn to the agent holding the write
/// tools" has to know a position the framework, not the application, decides.
/// The conversion goes through <see cref="HandoffToolName"/> — the same helper the
/// report uses to attribute a real handoff to a capability — so a test and the
/// production signal cannot disagree about which number means which capability.
/// </remarks>
internal static class HandoffScripting
{
    /// <summary>
    /// Routes the turn to the specialist bound to <paramref name="participantSlug"/>.
    /// </summary>
    public static FakeChatClient HandOffTo(
        this FakeChatClient fake,
        IServiceProvider provider,
        string participantSlug)
    {
        var catalog = provider.GetRequiredService<ISpecialistCatalog>();
        var order = catalog.Specialists
            .Where(s => s.IsExtracted)
            .Select(s => s.AgentSlug!)
            .ToList();

        var index = order.IndexOf(participantSlug);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"'{participantSlug}' is not a participant in this graph. Extracted: {string.Join(", ", order)}.");
        }

        fake.AutoHandoff = true;
        fake.AutoHandoffParticipant = index + 1;
        return fake;
    }

    /// <summary>
    /// The write capability's entry, by name. The tool it is keyed on is a
    /// discriminator for the fake's per-agent scripts, not part of the handoff.
    /// </summary>
    public static FakeChatClient HandOffToCreation(this FakeChatClient fake, IServiceProvider provider)
        => fake.HandOffTo(provider, AgentIds.Creation);

    /// <summary>The read capability's entry, by name.</summary>
    public static FakeChatClient HandOffToRetrieval(this FakeChatClient fake, IServiceProvider provider)
        => fake.HandOffTo(provider, AgentIds.Retrieval);
}
