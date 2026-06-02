# llm-unit-test-generator

Local C# webhook service that listens for GitHub push events on `main` in `No-Test-Application`, generates xUnit tests for all discovered API endpoints via Ollama (`codellama:7b`), and pushes the generated tests to a long-lived `Main-tested` branch.

## How it works

1. Receives `push` webhook events from GitHub.
2. Verifies `X-Hub-Signature-256` (if a webhook secret is configured).
3. Filters to the configured repository and `main` branch only.
4. Clones/updates the target repository using your PAT token.
5. Discovers ASP.NET Core controller endpoints.
6. Calls local Ollama (`http://localhost:11434`) to generate xUnit tests.
7. Writes tests into `tests/GeneratedApiTests/Generated`.
8. Commits and pushes to `Main-tested`.

## Prerequisites

- .NET SDK (matching your local installation)
- Git installed and available on PATH
- Local Ollama running with model `codellama:7b`
- GitHub PAT with access to clone/push `No-Test-Application`

## Configuration

Set values in:

- `LlmUnitTestGenerator/appsettings.json`
- or override with environment variables (recommended for PAT)

Required keys:

- `Generator:GitHub:RepositoryFullName` (example: `your-user/No-Test-Application`)
- `Generator:GitHub:RepositoryCloneUrl` (example: `https://github.com/your-user/No-Test-Application.git`)
- `Generator:GitHub:PatToken`
- `Generator:GitHub:WebhookSecret` (must match GitHub webhook secret)

Defaults already set for your choices:

- Trigger branch: `main`
- Output branch: `Main-tested`
- Ollama URL: `http://localhost:11434`
- Ollama model: `codellama:7b`
- Test framework: xUnit

## Run locally

```bash
cd LlmUnitTestGenerator
dotnet run
```

The webhook endpoint is:

- `POST /webhook/github`

Example local URL for GitHub webhook config (if using a tunnel):

- `https://<your-tunnel>/webhook/github`

## GitHub webhook setup

On `No-Test-Application` repository:

1. Go to **Settings → Webhooks → Add webhook**
2. Payload URL: your running service URL + `/webhook/github`
3. Content type: `application/json`
4. Secret: match `Generator:GitHub:WebhookSecret`
5. Event: **Just the push event**
6. Active: enabled

## Notes

- The service intentionally regenerates tests for all discovered endpoints on each `main` push.
- Generated tests are force-updated on `Main-tested` using `--force-with-lease`.
- Keep PAT out of source control (environment variables recommended).
