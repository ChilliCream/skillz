<p align="center">
  <img src="https://raw.githubusercontent.com/ChilliCream/skillz/main/assets/logo.svg" alt="Skillz" width="120" height="120" />
</p>

# skillz

A CLI for managing AI agent skills.

Skills are markdown files (`SKILL.md`) with YAML frontmatter that teach AI coding
agents how to do something. `skillz` installs them from GitHub, GitLab, generic Git
repos, or local paths - into whichever agents you have on your machine.

## Install

### Run without installing (`dnx`)

If you have the **.NET 10 SDK** (or newer), `dnx` runs the tool one-shot - no
`PATH` shim, no global state:

```bash
dnx skillz add chillicream/agent-skills
```

`dnx` is a shell script that ships with the SDK; it forwards to
`dotnet tool exec`, which downloads the package into the NuGet cache and runs
it. Subsequent runs hit the cache and are immediate.

**First run prompts:**

```
Tool package skillz@1.0.0 will be downloaded from source
https://api.nuget.org/v3/index.json. Proceed? [y/n] (y):
```

Pass `--yes` to skip the prompt (useful in CI):

```bash
dnx --yes skillz add anthropics/skills
```

**Pin a version** with `@`:

```bash
dnx skillz@1.0.0 add anthropics/skills
dnx skillz@1.* add anthropics/skills      # latest 1.x
```

**Allow prereleases:**

```bash
dnx --prerelease skillz add anthropics/skills
```

**Use a custom feed:**

```bash
dnx --source https://my.feed/v3/index.json skillz add ...
```

If a `.config/dotnet-tools.json` manifest is in scope, `dnx` uses the version
pinned there instead of the latest - handy for repo-local tool versions.

`dnx skillz` and `dotnet tool exec skillz` are equivalent; `dnx` is just the
shorter form.

### Persistent install

To put `skillz` on your `PATH` for repeated use:

```bash
dotnet tool install -g skillz
```

Requires the .NET SDK (8.0 or newer).

## Quick start

```bash
# Install all skills from a GitHub repo into Claude Code
dnx skillz add chillicream/agent-skills --agent claude-code

# Pick specific skills interactively
dnx skillz add chillicream/agent-skills

# Install one skill into multiple agents
dnx skillz add chillicream/agent-skills --skill code-review --agent claude-code --agent cursor

# Install everything into every detected agent, no prompts
dnx skillz add chillicream/agent-skills --all

# List what's installed
dnx skillz list

# Update installed skills to the latest version
dnx skillz update

# Remove a skill
dnx skillz remove code-review

# Scaffold a new skill
dnx skillz init my-skill
```

## Sources

`skillz add <source>` understands several source forms:

| Form                                 | Example                                  |
|--------------------------------------|------------------------------------------|
| `owner/repo` (GitHub)                | `skillz add anthropics/skills`           |
| Full Git URL                         | `skillz add https://github.com/owner/repo` |
| GitLab project                       | `skillz add gitlab:group/project`        |
| Local directory                      | `skillz add ./my-skills`                 |

By default skillz performs a shallow clone for speed. Pass `--full-depth` to clone
full history.

## Scope: project vs. global

Without `--global`, skills are added to the current project (recorded in
`skills-lock.json` in the working directory). With `--global`, they're installed
once for your user under your XDG data dir (`$XDG_DATA_HOME/skillz` or
`~/.local/share/skillz`), with the global lock file (`.skill-lock.json`) kept
alongside the installed skills in that same directory.

## Agents

`skillz` supports 55+ AI coding agents - Claude Code, Cursor, GitHub Copilot,
Codex, Continue, Gemini CLI, and many more. Detection is automatic; the
`--agent <name>` flag (repeatable) targets specific ones. Run `skillz list` after
install to see which agents on your machine were updated.

By default skills are **symlinked** from a canonical location so editing one place
updates every agent. Pass `--copy` to copy files instead - useful for sandboxed
agents that don't follow symlinks.

## Authoring a skill

```bash
skillz init my-skill
```

…creates `my-skill/SKILL.md` with the right frontmatter shape:

```markdown
---
name: my-skill
description: A brief description of what this skill does
---

# my-skill

Instructions for the agent to follow when this skill is activated.
```

Push the file to a public repo and anyone can install it with
`skillz add <owner>/<repo>`.

## Commands

| Command                       | What it does                                       |
|-------------------------------|----------------------------------------------------|
| `skillz add <source>`         | Install skill(s) from a source                     |
| `skillz remove <skills...>`   | Uninstall skill(s)                                 |
| `skillz list`                 | List installed skills                              |
| `skillz update [skills...]`   | Update installed skills (alias `upgrade`, `check`) |
| `skillz init [name]`          | Create a new `SKILL.md` scaffold                   |

Common flags: `--global / -g`, `--agent / -a <name>`, `--skill / -s <name>`,
`--yes / -y`, `--all`, `--copy`, `--full-depth`, `--list / -l`, `--json`.

`skillz <command> --help` shows the full option list for each command.

## Source code

<https://github.com/chillicream/skillz>

## License

MIT - see [LICENSE](https://github.com/chillicream/skillz/blob/main/LICENSE).
