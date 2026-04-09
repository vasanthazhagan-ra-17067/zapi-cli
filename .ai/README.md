# .ai — AI Context for zapi-cli

**Project**: zapi-cli
**Description**: Standalone cross-platform CLI binary providing a scriptable interface to any Zoho product's REST APIs. Primary consumer is AI agents (GitHub Copilot CLI Skills, Claude Agent Skills).
**Language / Runtime**: C# 13+ / .NET 10 — self-contained single-file binaries targeting win-x64, osx-x64, osx-arm64, linux-x64
**Repository**: zapi-cli
**Owner**: zapi-cli team

> This folder is the shared context layer for all AI agents working in this project.
> Load the `ai-folder-knowledge` skill for folder map, file descriptions, and navigation guide.

## Folder Reference

| Folder / File | Purpose |
|---------------|---------|
| `project-context.json` | Static project metadata — name, description, solution layout, auth model, output contract, scope boundaries, DC map |
| `agent-memory.json` | Execution history (completed stories + files touched) — append-only |
| `open-questions.json` | Open questions in OQ-NNN format with default assumptions |
| `dependency-map.json` | Story dependency graph — storyId → dependsOn[] |
| `architecture-reference.json` | Project-specific architecture snapshot — command tree, interfaces, data models, error codes |
| `plans/` | Implementation plans — one `kebab-name-N.md` file per plan |
| `stories/` | User stories — `story-NN.json`, globally numbered (currently story-01 through story-41) |
| `docs/` | General reference docs — command reference, auth docs, OAuth specs, ADR research |
| `spec/brainstormings/` | Brainstorming documents |
| `spec/prds/` | Product Requirements Documents |
| `spec/tech-specs/` | Technical Specification documents |
| `spec/adr/` | Architecture Decision Records — `adr-NNNN-kebab-name.md` (adr-0001 through adr-0008) |
| `features/` | Feature folders; each contains numbered phase files |
