# Project Guidelines — zapi-cli

Standalone cross-platform CLI binary for interacting with any Zoho product's REST APIs. Primary consumers are AI agents and shell scripts. Written in C# 13+ / .NET 10, published as self-contained single-file binaries for 6 platforms.

## Build and Test

```bash
# Build
dotnet build src/zapi-cli.sln -c Release

# Run tests
dotnet test tests/ZapiCli.Tests/ZapiCli.Tests.csproj

# Publish all platforms (osx-x64, osx-arm64, win-x64, win-arm64, linux-x64, linux-arm64)
./publish-all.sh

# Run locally (macOS ARM)
./build/osx-arm64/zapi --help
```

## Architecture

Three projects in `src/zapi-cli.sln`:

| Project | Role |
|---------|------|
| `ZapiCli` | Entry point, commands (thin layer), DI wiring |
| `ZapiCli.Core` | Business logic, services, models, interfaces |
| `ZapiCli.Keychain` | Platform-specific credential storage (macOS Keychain, Windows Credential Manager, Linux Secret Service, AES-256-GCM fallback) |

**CLI framework**: Spectre.Console.Cli v0.49.1 — commands inherit `AsyncCommand<TSettings>` with `GlobalSettings` base class providing shared flags (`--account`, `--json`, `--no-input`).

**Key architectural rules**:
- Commands are a thin layer: validate flags → delegate to service → output via `IOutputWriter`. Never call `Console` directly.
- All output goes through `IOutputWriter` → `JsonOutputWriter` (stdout for data, stderr for errors). This preserves the JSON stdout contract for scripting and AI agents.
- JSON serialization uses `SnakeCaseLower` naming, null values omitted, compact format.
- Errors use `ZapiCliException` with codes from `ErrorCodes.cs`. Global handler catches and writes JSON error envelope to stderr.
- Logging is Warning+ level, routed to stderr only.

**Security (non-negotiable)**:
- `ZohoCorpGuard.AssertNotZohoCorp()` — hard-blocks zohocorp email domains. Must be called at account add, API call, and scope commands. See ADR-0003.
- `HostValidator` — allowlist-only: `*.zoho.com`, `*.zoho.eu`, `*.zohoapis.com`. Prevents SSRF. See ADR-0004.
- Trace writer filters security headers (`Authorization`, `Cookie`, `Set-Cookie`, `WWW-Authenticate`).
- Account files use restrictive permissions (0600 Unix).

## Conventions

### Command pattern

```csharp
public sealed class FooSettings : GlobalSettings
{
    [CommandOption("--bar")] public string? Bar { get; init; }
    public override ValidationResult Validate() { /* ... */ }
}

public sealed class FooCommand : AsyncCommand<FooSettings>
{
    public FooCommand(IFooService svc, IOutputWriter output) { /* DI */ }
    public override async Task<int> ExecuteAsync(CommandContext ctx, FooSettings settings)
    {
        var result = await _svc.DoAsync(settings.Bar);
        _output.WriteJson(result);
        return 0;
    }
}
```

Register in `Program.cs` via `app.AddCommand<>()` or `app.AddBranch<>()`.

### Error handling

```csharp
throw new ZapiCliException("message", ErrorCodes.SYMBOLIC_CODE, exitCode: 1);
```

Add new codes to `ErrorCodes.cs` as static string fields.

### Output contract (ADR-0008)

- Success: `{ "status": "ok", "data": { ... } }` (stdout)
- Error: `{ "error": "...", "code": "...", "exitCode": N }` (stderr)

### Testing

- Framework: xUnit 2.9.3. Test naming: `MethodName_Condition_Expected`.
- Each test creates an isolated temp directory (cleanup via `IDisposable`).
- Use `Fake*` classes from `tests/ZapiCli.Tests/Fakes/` — no mocking frameworks. Fakes exist for: `AccountStore`, `AuthProvider`, `HttpMessageHandler`, `OAuthBrowserFlow`, `LocalCallbackServer`, `RsaKeyPairProvider`, `TraceWriter`, `KeychainProvider`.
- HTTP faking: `FakeHttpMessageHandler.ToFactory(request => response)`.
- Assertions: xUnit built-in (`Assert.Equal`, `Assert.Throws<T>`). No external assertion libraries.

## AI Context Folder (`.ai/`)

The `.ai/` folder is the shared context layer for all AI agents. Load the `ai-folder-knowledge` skill for the full folder map and file descriptions.

| File / Folder | Purpose |
|---------------|---------|
| `project-context.json` | Static metadata — name, stack, solution layout, auth model, output contract |
| `agent-memory.json` | Append-only execution history of completed stories, files touched, decisions |
| `architecture-reference.json` | Architecture snapshot — command tree, interfaces, error codes, data models |
| `open-questions.json` | Unresolved questions (OQ-NNN format) with default assumptions |
| `dependency-map.json` | Story dependency graph (storyId → dependsOn[]) |
| `stories/` | User stories — `story-NN.json`, globally numbered |
| `plans/` | Implementation plans — `kebab-name-N.md` |
| `spec/adr/` | Architecture Decision Records — `adr-NNNN-kebab-name.md` (0001–0008) |
| `spec/prds/` | Product Requirements Documents |
| `spec/tech-specs/` | Technical Specifications |
| `docs/` | Reference docs (auth, OAuth specs, command reference) |

**When implementing a story**: read the story JSON, check `dependency-map.json` for blockers, update `agent-memory.json` after completion.

**When making architectural decisions**: check existing ADRs in `.ai/spec/adr/` first; create a new ADR for significant choices.

## Key References

- [README.md](../README.md) — installation, output contract, full command reference, error handling
- [docs/HELP.md](../docs/HELP.md) — extended command reference and examples
- [.ai/README.md](../.ai/README.md) — AI context folder navigation guide
- `.github/skills/zapi-cli/SKILL.md` — zapi-cli binary skill reference for AI agents
