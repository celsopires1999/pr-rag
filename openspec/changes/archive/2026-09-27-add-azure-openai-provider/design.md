## Context

The entire provider surface of this system is two constructor calls:

```csharp
var chatClient = new ChatClient(openAi.ChatModel, openAi.ApiKey);
var embeddingClient = new EmbeddingClient(openAi.EmbeddingModel, openAi.ApiKey);
```

Everything downstream of those lines is already provider-neutral. `ChatService`,
`AgentGraphComposer`, `AgentAttributionChatClient`, the MAF workflow, and every
specialist speak only `IChatClient` and `IEmbeddingGenerator<string, Embedding<float>>`.
`AgentAttributionChatClient` is a `DelegatingChatClient` that never learns a model
name. The application layer resolves no provider package at all.

Three facts make this change smaller than it looks:

- **The OpenAI SDK already resolved here talks to Azure.** `OpenAI` 2.12.0 is on
  the graph (pulled by `Microsoft.Extensions.AI.OpenAI` 10.9.0). It exposes
  `OpenAIClientOptions.Endpoint`, and Azure OpenAI's `/openai/v1` surface has the
  same path shape as OpenAI's. The same `ChatClient` / `EmbeddingClient` types
  work against both; only the endpoint and the model string differ — and on Azure
  the model string is the **deployment name**. The SDK's own README documents
  this as the supported path, with a bearer-token policy for Entra ID.
- **No new package is needed for API-key auth.** `Azure.AI.OpenAI` is an
  extension over the same `OpenAI` assembly and is the wrong dependency here:
  it would add an `Azure.Core` version floor and a second way to do something the
  SDK already does. Entra ID would need `Azure.Core` (and `Azure.Identity` for
  `DefaultAzureCredential`), which is why it is deferred rather than bundled.
- **The unkeyed `IChatClient` singleton is the contract with the application
  layer, and with the tests.** `AgentRunService` injects it unkeyed; the test
  factory satisfies it with `FakeChatClient` without a credential or a network.
  Any design that keys the registration, or that introduces a provider-specific
  `IChatClient` type, drags the entire test suite across a credential boundary
  for no gain.

What is genuinely missing is not code but configuration and diagnosis. `OpenAISettings`
has three properties and no endpoint, no deployment names, and no provider field.
`PurchaseRequisition.EmbeddingDimensions` is a domain constant of `1536` already
materialised as `vector(1536)` in the initial migration. And nothing anywhere
records which provider or model served a request — not `RagQueryReport`, not
`/api/status`, not the logs. `ChatService.RequireAnswer` throws
`ChatTurnFailedException` on an empty run, `/api/chat` maps it to `502`, and the
message points the operator at the log category
`Microsoft.Extensions.AI.LoggingChatClient` as the only place the cause will
appear. That is the entire provider diagnostic surface today.

The constraint that shapes the design most is that last one. A misconfigured
provider does not produce an exception at startup; it produces a client that
throws on the first request, which MAF absorbs into an empty run, which the API
reports as a `502` — the same signal a dead provider or a rejected key produces.
This change is what makes a *configuration* fault distinguishable from an
*outage*, and that is why validation is a startup concern rather than a
documentation concern.

## Goals / Non-Goals

**Goals:**

- A deployment selects Azure OpenAI by environment configuration alone, with no
  code change and no rebuild.
- OpenAI remains the default. An existing `.env` with no provider key set keeps
  working byte-for-byte.
- A provider misconfiguration fails the host at startup, naming the config key
  that is wrong, and is never reported as a `502` from a chat turn.
- The resolved provider, endpoint, and deployment names are visible in the startup
  log, so "which model is this actually talking to" has an answer.
- The application layer, the agent graph, the report, the API, the schema, and
  the credential-free test suite are untouched.

**Non-Goals:**

- **No Entra ID.** API-key auth only on both paths. Entra needs a credential
  policy, a token-expiry failure mode, and a dependency this change does not
  otherwise need; it is a clean follow-up because the provider branch is where
  auth would be chosen.
- **No mixed providers.** One provider per process, for both chat and embeddings.
  Chat-on-Azure with embeddings on OpenAI would mean two credentials, two failure
  modes, and a half-swapped system whose only symptom is that search results stop
  matching.
- **No `Azure.AI.OpenAI` package, and no new package at all.**
- **No renaming of the `OpenAI` configuration section.** Discussed below.
- **No change to embedding dimensionality or the schema.** `EmbeddingDimensions`
  stays a domain constant. If an Azure embedding deployment does not produce
  1536-dimension vectors, that is a new migration and a separate change, exactly
  as it is for OpenAI.
- **No provider identity in `RagQueryReport`.** The report is a routing and
  write-attempt artefact; adding a provider field means touching `ChatService`,
  the DTO, and both live gate scripts, and nothing consumes it yet. A startup log
  line answers the operational question at a fraction of the cost. Recorded as an
  open question rather than dismissed.
- **No test-suite change.** Provider resolution is tested directly, with no
  database, no network, and no credential.

## Decisions

**The provider branch lives in a dedicated Infrastructure component, and
`DependencyInjection` keeps only the registration.** The alternative — an `if`
inside `AddInfrastructure` — is four extra lines and is the shape that produced
the original coupling. The deciding factor is where the next provider lands. The
construction site is already the only OpenAI-specific code in the solution, and
keeping it as a named component means the next provider is a new case in one file
rather than a wider conditional in a method that also configures the database,
the repositories, and two hosted services. The component resolves settings into a
validated value and builds the two clients; `AddInfrastructure` still calls
`AddChatClient(...).UseLogging()` and `AddEmbeddingGenerator(...).UseLogging()`
and changes nothing else.

**Endpoint resolution is a pure function returning a `Uri`, validated for shape,
and it is the only place a `Uri` is produced.** The `OpenAIClientOptions.Endpoint`
that the client consumes is write-only — it is not readable back off a built
`ChatClient` — so a test cannot assert it after the fact. Returning the `Uri`
from the resolution step and handing that to the client makes the value itself
assertable, and makes endpoint validation testable without a client or a
network. Shape validation is not decoration: Azure's `/openai/v1` suffix is
required, and a bare `https://{resource}.openai.azure.com` returns a `404` from a
path that does not exist, which is again indistinguishable from a dead provider.

**Validation throws at startup rather than defaulting, and it names the missing
key.** The failure this prevents is specific: `Provider=azure` with no
`Endpoint` currently produces a client pointed at OpenAI's default endpoint with
an Azure key, which fails per-request and surfaces as a `502` on the first chat
turn — the signature of an outage, and the reason this repo once reported a
completely dead provider as eight clean runs. The same treatment applies to a
blank API key on either path and to an unrecognised provider name, which today
would be silently ignored. The throw happens in the same place and in the same
style as the existing `ConnectionStrings__Default` check, so the failure mode is
one this codebase already produces and operators already read.

**Compose stops requiring the key, and the application takes over that
responsibility.** `docker-compose.yml` currently uses
`${OpenAI__ApiKey:?...}`, which hard-fails interpolation when the variable is
absent. With two providers the required credential has two possible names, and
compose can require only one — so a rule that is correct for the OpenAI path
becomes a boot failure for the Azure path. Relaxing it to a pass-through and
letting the application's startup validation produce the error keeps a single
source of truth for "a credential is required", and produces a better message
because it can name the provider that is missing one. This does not weaken the
`demo` profile, which exists for an unrelated and still-valid reason: a profile
gate is what keeps the credential out of `docker compose config` output, and
relaxing the interpolation form does not change what that command prints.

**The configuration section keeps the name `OpenAI`.** Renaming it to something
provider-neutral would be tidier and would break every existing `.env`, every
compose service, `.vscode/launch.json`'s debug environment, and the README config
table, in exchange for accuracy in a name. The keys also remain literally true:
Azure OpenAI is reached through the OpenAI SDK, and the models are still OpenAI
models — only the deployment and the host differ. The cost is a section called
`OpenAI` that can select Azure, which is a documentation obligation rather than a
correctness one, and the doc note in this change is where that obligation is met.
Deferred deliberately, not overlooked.

**On Azure, `ChatModel` and `EmbeddingModel` are deployment names, and the
setting names do not change to say so.** Deployment names on Azure are arbitrary
— `gpt-4o-mini-prod` is as valid as `gpt-4o-mini` — so the same setting that
means "model" on one provider means "deployment" on the other. Renaming the
properties to `ChatDeployment` would be wrong on the OpenAI path; keeping
`ChatModel` and documenting the Azure reading is the smaller and more honest
cost. The startup log prints the resolved values precisely because the
configuration no longer tells you which of the two meanings is in play.

**The embedding dimension constraint is documented, not enforced.** A
dimension-mismatched Azure deployment fails at the database with a `vector(1536)`
dimension error, on the ingest path, not at startup — and an operator will read
that as a data bug. A startup probe would need a live call to the deployment and
its cost is not justified for a misconfiguration that is rare, loud, and
diagnosed by one log line. The constraint is stated in `.env.example`, the
README config table, and the existing `AGENTS.md` note that the dimension is
coupled to the model, so the answer is where the setting is.

**The test suite is not made provider-aware.** `IntegrationServiceFactory` never
calls `AddInfrastructure`; it registers `FakeChatClient` under the unkeyed
`IChatClient` and `FakeEmbeddingService` under `IEmbeddingService`. The new
component is tested on its own: resolution accepts each provider, rejects each
malformed input by name, and returns the expected `Uri`. What is **not** testable
in process is that the endpoint reaches a real Azure resource or that a
deployment name is correct — no fake model can stand in for a provider, and a
fabricated `IChatClient` asserting its own endpoint would assert nothing. That
part is a live task against a real resource, stated as such rather than papered
over with a test that cannot fail.

## Risks / Trade-offs

- **A wrong deployment name is still indistinguishable from a dead provider.**
  Azure returns a provider error for an unknown deployment, MAF absorbs it into an
  empty run, and `/api/chat` returns `502`. Mitigation: the startup log prints
  both resolved deployment names so the configuration can be checked before the
  first turn rather than inferred from one, and the same `UseLogging()` decorator
  is retained so the cause is still in the log category `RequireAnswer` names.
  This is a known limit, not a solved problem.
- **Two providers means two credential names in `.env`, and only one is set at a
  time.** A stale `OpenAI__ApiKey` left beside a new Azure key is not detected.
  Mitigation: the startup log prints the provider that was selected, so the
  mismatch is visible immediately; a stricter check that rejects an
  endpoint-bearing config carrying an OpenAI-shaped key is not added, because the
  two keys are the same shape and indistinguishable by inspection.
- **The application layer has no compile-time signal that a second provider
  exists.** Nothing in `Application` can drift, because it references no provider
  type, but nothing there asserts it either. Mitigation: unchanged, and correct —
  the layering invariant is already that `PrRag.Application` resolves no provider
  package, and this change preserves it rather than adding a check for a property
  that was already true.
- **A user can select Azure and leave the OpenAI model defaults in place.**
  `gpt-4o-mini` is not necessarily a deployment name on their resource.
  Mitigation: the startup log prints the resolved values, and the documentation
  states that the Azure path takes deployment names. Deliberately not defaulted
  away — a default that silently pointed at a nonexistent deployment would be the
  same failure one step later.
- **Two live gates reason about model behaviour and now have a second model to
  reason about.** A rate measured on OpenAI does not transfer to an Azure
  deployment, and the `MIN_RATES` floors are model-dependent by construction.
  Mitigation: the startup log identifies the provider a gate run was made
  against, so a floor is never silently compared across providers. No floor is
  changed by this change.
- **Endpoint shape validation could reject a valid future Azure URL.** The check
  asserts the `/openai/v1` suffix the current API requires. Mitigation: the check
  is one named predicate with a comment naming the surface it encodes, so a future
  shape is a one-line change rather than a hunt.

## Migration Plan

No data, schema, endpoint, or API migration. The rollout is additive: two
optional settings with defaults that reproduce today's behaviour exactly. An
existing deployment sets nothing and is unchanged. An Azure deployment sets
`Provider`, `Endpoint`, `ApiKey`, and both deployment names, and rebuilds nothing.

Rollback is deleting the provider and endpoint values from the environment. That
returns the process to the OpenAI default, which is the configuration that is in
use today and is known to work — so rollback does not depend on reverting the
code.

Switching an existing deployment *to* Azure is not a configuration-only operation
if the embedding model changes dimensionality: the existing rows were embedded
with a 1536-dimension model and the column is `vector(1536)`. Pointing
`EmbeddingModel` at a 1536-dimension Azure deployment and re-running ingestion is
a data operation, not a config operation, and is out of this change's scope.

## Open Questions

- Should `RagQueryReport` carry the provider and deployment names? The startup log
  answers "what is this talking to" for an operator, but a report written per turn
  would answer it retrospectively, which is what the two live gates would need if
  a run is ever made against a second provider and compared. Deferred because
  nothing consumes it yet, and the answer changes if the gates ever run against
  both providers.
- Should Entra ID be added as a third auth mode in the provider branch, or as a
  separate credential concept orthogonal to the provider? The second is cleaner
  once there are two providers, because auth and host are independent axes. Worth
  deciding when Entra is requested rather than guessing now.
- Should a `data/skills`-style mechanism expose the deployment names to the
  orchestrator's skill manifest? No current requirement asks for it, and the
  manifest is about capabilities rather than infrastructure. Raised only to
  record that it was considered and rejected.
- Does the `devcontainer` service need the new provider keys passed through for
  F5 debugging against Azure? Today it deliberately carries no credential and the
  debug launch reads `.env` through `envFile`, so the answer is probably yes via
  the same route — but the `devcontainer-environment` invariant has to be restated
  to permit it without opening the door to a credential in the service
  environment.
