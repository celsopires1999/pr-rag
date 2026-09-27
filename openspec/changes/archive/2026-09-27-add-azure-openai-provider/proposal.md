## Why

The LLM provider is hardcoded to OpenAI in two constructor calls, so the only way
to run this system against Azure OpenAI is to fork `DependencyInjection`. Azure
is the deployment most enterprises are required to use, and "we cannot run the
demo where our data-residency policy applies" is a real blocker rather than a
nicety. The change is cheap because the provider surface is already almost
entirely abstracted: the whole application layer speaks `IChatClient` and
`IEmbeddingGenerator`, and the OpenAI SDK version already resolved here talks to
Azure OpenAI natively over its `/openai/v1` endpoint. Only the construction of
the two clients is OpenAI-specific, and only the *settings* are missing an
endpoint.

## What Changes

- Add a provider discriminator and an optional endpoint to `OpenAISettings`, so a
  deployment selects Azure OpenAI by configuration rather than by code change.
- Move client construction out of `DependencyInjection` into a dedicated
  Infrastructure component that owns the provider branch. `DependencyInjection`
  keeps registering the same two abstractions.
- Resolve and validate the provider configuration **at startup**. A missing
  endpoint, a malformed endpoint, an unknown provider name, or a blank API key
  fails the host at composition time with a message naming the config key, rather
  than surfacing later as a `502` on the first chat turn.
- Log the resolved provider, endpoint, and both deployment names once at
  startup. Nothing in the system currently records which provider or model served
  a request, so a misconfigured swap is invisible until an operator reads a
  failure.
- Support API-key auth on the Azure path only. Entra ID is deferred.
- Thread `Provider` and `Endpoint` through `docker-compose.yml` and
  `.env.example`, and relax compose's hard `OpenAI__ApiKey` requirement to a
  pass-through, because the required credential is now provider-dependent and
  compose can only require one name.
- Document that on Azure the `ChatModel` and `EmbeddingModel` values are
  **deployment names**, and that the Azure embedding deployment must produce
  1536-dimension vectors or the existing `vector(1536)` column rejects them.

Nothing outside Infrastructure changes. The application layer, the agent graph,
the observability report, the API surface, the database schema, and the entire
test suite are untouched — the unkeyed `IChatClient` singleton stays the seam, so
`FakeChatClient` keeps satisfying it and no test needs a credential.

## Capabilities

### New Capabilities

- `llm-provider-configuration`: selecting the LLM provider by configuration,
  resolving its endpoint and deployment names, validating that configuration
  before the host starts, and recording the resolved provider so a swap is
  diagnosable. Also covers the secret-handling invariants that now span two
  possible providers.

### Modified Capabilities

- `chat-query`: "RAG controls configured via environment" currently says the
  system "authenticates against the OpenAI API". That is the only statement in
  the specs that pins the provider, and it becomes false the moment an Azure
  deployment is supported. The requirement keeps its shape — models and
  retrieval parameters stay configurable, the key stays out of the repository —
  and stops naming one vendor.
- `devcontainer-environment`: "Secrets not exposed to the container environment"
  guards "the OpenAI API key". With a second provider there is a second possible
  credential, and as written the invariant reads as covering only the first. The
  invariant is unchanged in intent; it is restated so that it covers every
  provider credential rather than one of them.

## Impact

- `Application/Configuration/OpenAISettings.cs` — gains `Provider` and
  `Endpoint`. The section name stays `OpenAI`, so no existing `.env`, compose
  file, `launch.json`, or README row changes and the default remains OpenAI with
  no endpoint.
- `Infrastructure/DependencyInjection.cs` — the two constructor calls at lines
  37-38 are replaced by a call into the new component, plus the startup
  validation. The `AddChatClient(...).UseLogging()` and
  `AddEmbeddingGenerator(...).UseLogging()` registrations are unchanged and must
  stay: `ChatService.RequireAnswer` names
  `Microsoft.Extensions.AI.LoggingChatClient` as the operator's only path to the
  cause of an empty run, so that decorator is load-bearing, not decorative.
- New Infrastructure types for provider resolution and client construction. No new
  NuGet package: the Azure path is reached through the `OpenAI` SDK already
  referenced, and API-key auth needs no `Azure.Core` credential policy.
- `tests/PrRag.Tests/IntegrationServiceFactory.cs` — unchanged. It never calls
  `AddInfrastructure` and registers `FakeChatClient` under the unkeyed
  `IChatClient`, which is why a provider swap does not reach the test suite. New
  tests cover provider resolution directly, with no database and no credential.
- `docker-compose.yml`, `.env.example`, `README.md`, `AGENTS.md` — documented
  configuration surface. The `demo` profile gating is unchanged and still
  required: a second provider does not make it safe to print credentials.
- **Not** verifiable in process: that the endpoint actually reaches an Azure
  resource and that a deployment name is correct. A wrong deployment name returns
  a provider error indistinguishable from a dead provider, which is precisely
  what the startup logging and endpoint-shape validation exist to make
  diagnosable. Verification of that part is a live task against a real resource.
