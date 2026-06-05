# Skillz

A CLI for managing AI agent skills - markdown `SKILL.md` files with YAML
frontmatter that teach AI coding agents how to do specific tasks.

```bash
# One-shot run, no install (.NET 10+ SDK)
dnx skillz add chillicream/agent-skills

# Or persistent install
dotnet tool install -g skillz
```

End-user documentation - including the full `dnx` reference - lives in
[`src/Skillz.Tool/README.md`](src/Skillz.Tool/README.md) and is what ships on
NuGet.

## What it does

- Installs skills from GitHub, GitLab, generic Git repos, or local paths
- Targets 55+ AI coding agents (Claude Code, Cursor, Copilot, Codex, Continue,
  Gemini CLI, …) - auto-detected on your machine
- Symlinks by default from one canonical location so all agents stay in sync;
  `--copy` for agents that don't follow symlinks
- Two scopes: project (`skills-lock.json` in cwd) and global (XDG state dir)
- Scaffolds new skills with `skillz init`

## Repository layout

```
src/
  Skillz/           Main CLI assembly (AOT-publishable binary)
  Skillz.Tool/      `dotnet tool` wrapper that ships as the `skillz` NuGet package
test/
  Skillz.Tests/     Unit tests
  Skillz.SmokeTests/ End-to-end smoke tests
Skillz.sln          Solution
global.json         .NET SDK pin
```

`Skillz.Tool` is a thin wrapper that calls into `Skillz.Program.Main`. It exists
so `dotnet tool install -g skillz` works while `Skillz` itself can also be
AOT-published as a standalone binary for the supported runtime identifiers
(`linux-x64`, `linux-arm64`, `linux-musl-x64`, `osx-x64`, `osx-arm64`, `win-x64`,
`win-arm64`).

## Build

```bash
dotnet build
```

Targets `net8.0` and `net9.0`. Requires the .NET SDK pinned in `global.json`.

## Test

```bash
dotnet test
```

## Run locally

```bash
dotnet run --project src/Skillz -- add anthropics/skills
```

## Publish AOT

```bash
dotnet publish src/Skillz -c Release -r linux-x64
```

Produces a single self-contained `skillz` binary at
`src/Skillz/bin/Release/<tfm>/linux-x64/publish/skillz`.

## Pack the tool

```bash
dotnet pack src/Skillz.Tool -c Release -o ./artifacts
```

Produces `artifacts/skillz.<version>.nupkg` ready for `dotnet nuget push`.

## Architecture

The CLI is built on `Microsoft.Extensions.Hosting` + `System.CommandLine` +
`Spectre.Console`, with AOT-safe JSON via `System.Text.Json` source generators.
Commands inherit from a `BaseCommand` template method; cross-cutting state lives
on a single `CliExecutionContext`; user interaction goes through
`IInteractionService`.

See [`aspire-cli-pattern.md`](aspire-cli-pattern.md) for an in-depth look at the
architectural patterns this CLI is modeled on, distilled from the Microsoft
Aspire CLI source.

## Contributing

Issues and PRs welcome. Before sending a PR:

```bash
dotnet build
dotnet test
```

## License

[MIT](LICENSE)
