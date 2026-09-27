## ADDED Requirements

### Requirement: The LLM provider is selected by configuration
The system SHALL determine the LLM provider from configuration at startup rather
than from a hardcoded constructor, and SHALL accept a provider discriminator and
an optional endpoint alongside the chat and embedding model settings, so that a
deployment is pointed at a different provider by environment values alone. The
provider discriminator SHALL default to OpenAI when unset, and the endpoint SHALL
be unset by default, so a deployment that configures nothing behaves exactly as
it does today. One provider SHALL serve both the chat and the embedding client;
the system SHALL NOT support a configuration in which the two come from
different providers.

#### Scenario: No provider configured
- **WHEN** the system starts with no provider discriminator set
- **THEN** the OpenAI provider is selected with its default endpoint, using the configured chat model, embedding model, and API key

#### Scenario: Azure OpenAI selected
- **WHEN** the system starts with the provider discriminator set to Azure and an endpoint, chat deployment, and embedding deployment configured
- **THEN** both the chat client and the embedding client are constructed against that endpoint, using the configured values in place of the model names

#### Scenario: One provider serves chat and embeddings
- **WHEN** a provider is selected
- **THEN** the chat client and the embedding client are both constructed from that provider's resolved configuration, so a single process never holds credentials for two providers

#### Scenario: The selected provider is the only credential required
- **WHEN** the Azure provider is selected
- **THEN** no OpenAI-specific credential is required to start, and the credential required is the one belonging to the selected provider

#### Scenario: Deployment names are supplied in place of model names
- **WHEN** the Azure provider is selected
- **THEN** the values configured as the chat and embedding models are used verbatim as the provider's deployment identifiers, without being validated against a list of known model names

### Requirement: Provider configuration is validated before the host serves a request
The system SHALL validate the resolved provider configuration during service
registration and SHALL fail host startup with an error naming the configuration
key at fault, rather than constructing a client that fails on first use. A
configuration fault SHALL NOT be reported as a failed chat turn. Validation SHALL
cover, at minimum: an unrecognised provider discriminator, a missing endpoint for
a provider that requires one, an endpoint that does not carry the path shape that
provider requires, and a blank API key on any provider.

#### Scenario: An unknown provider is rejected by name
- **WHEN** the provider discriminator is set to a value the system does not recognise
- **THEN** startup fails with an error naming the provider setting and listing the recognised values

#### Scenario: A required endpoint is missing
- **WHEN** a provider that requires an endpoint is selected and no endpoint is configured
- **THEN** startup fails with an error naming the endpoint setting, rather than falling back to the provider's default host

#### Scenario: An endpoint with the wrong path shape is rejected
- **WHEN** the Azure provider is selected with an endpoint that omits the versioned path the Azure OpenAI API requires
- **THEN** startup fails with an error naming the endpoint setting, because the resulting request would fail against a path that does not exist

#### Scenario: A blank credential is rejected
- **WHEN** the resolved API key is empty or whitespace on any provider
- **THEN** startup fails with an error naming the API key setting, rather than deferring the rejection to the first request

#### Scenario: An endpoint set for a provider that does not use one is refused
- **WHEN** an endpoint is configured while the selected provider has its own default host
- **THEN** startup fails with an error naming the endpoint setting, rather than ignoring the value and sending requests to the provider's default host, because an operator who set an endpoint expects the requests to go there

#### Scenario: A configuration fault is not reported as a chat failure
- **WHEN** provider configuration is invalid
- **THEN** the host does not start, so the fault cannot surface as a chat request that fails against the configured provider, which is indistinguishable from a provider outage

#### Scenario: The endpoint is validated independently of the client it configures
- **WHEN** the endpoint is resolved
- **THEN** the resolved endpoint is a value the system produced and can assert on, rather than a write-only option read back from a constructed client

### Requirement: The resolved provider is identifiable from the startup log
The system SHALL log the resolved provider, endpoint, chat deployment, and
embedding deployment once during startup, so that the provider and model serving
the system are answerable without reading the configuration that produced them.
The system SHALL NOT log the API key.

#### Scenario: Resolved provider is logged at startup
- **WHEN** the system starts with any valid provider configuration
- **THEN** a single log entry names the selected provider, the resolved endpoint, and both the chat and the embedding deployment in use

#### Scenario: Deployment names are logged because their meaning is provider-dependent
- **WHEN** the Azure provider is selected
- **THEN** the logged chat and embedding values are the deployment identifiers the system will request, so a configuration using model names where deployment names belong is visible before the first request

#### Scenario: The credential is not logged
- **WHEN** the resolved provider configuration is logged at startup
- **THEN** the entry contains no part of the API key, and the key remains available only through the environment

### Requirement: The provider choice does not reach the application layer
The system SHALL construct the chat client and the embedding client behind the
provider-neutral abstractions the application already consumes, and SHALL
register them as the same unkeyed singletons, so that the application layer, the
agent graph, and the observability report are identical across providers. The
application project SHALL resolve no provider-specific package. The logging
decorator on both clients SHALL be retained, because it is the documented
diagnostic path for a turn that produced no answer.

#### Scenario: The same abstractions are registered for every provider
- **WHEN** the system starts on either provider
- **THEN** the chat client is registered as the unkeyed `IChatClient` and the embedding client as the unkeyed embedding generator, with the logging decorator applied to both, so no consumer resolves a provider-specific type

#### Scenario: The application layer carries no provider knowledge
- **WHEN** the application project is inspected
- **THEN** it references no provider SDK or provider settings type beyond the provider-neutral configuration it binds, and no agent, tool, or service names a provider

#### Scenario: The empty-run diagnostic path is preserved
- **WHEN** a turn produces no answer and the resulting error names the logging category to consult
- **THEN** that category is still decorated onto the chat client on every provider, so the guidance in the error remains accurate

#### Scenario: The test suite requires no credential
- **WHEN** the test suite runs
- **THEN** it substitutes its own chat client and embedding service for the configured ones and reaches no provider, so no test requires an API key, an endpoint, or network access for any provider

### Requirement: A provider credential is supplied at runtime and never committed
The system SHALL read the provider credential from the runtime environment and
SHALL NOT require it to be present in any file tracked by the repository. Where
a deployment environment composes the service's configuration, it SHALL NOT make
a specific credential name mandatory at composition time, because the required
credential name depends on the selected provider; the requirement that one is
present SHALL be enforced by the application's startup validation instead.

#### Scenario: The credential comes from the environment
- **WHEN** the system starts
- **THEN** the API key is read from the runtime configuration and the repository contains no credential for any provider

#### Scenario: Composition does not mandate one credential name
- **WHEN** the deployment environment is composed
- **THEN** the configuration is accepted whether or not a provider credential is set, and a missing credential is reported by the application's startup validation naming the key

#### Scenario: Restricting where a credential is not provided does not restrict the profiles that gate it
- **WHEN** a provider credential is optional at composition time
- **THEN** the service that would print resolved configuration is still gated behind a profile, so relaxing the mandatory variable does not make the credential printable
