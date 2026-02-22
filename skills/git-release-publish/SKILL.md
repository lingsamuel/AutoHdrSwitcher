---
name: git-release-publish
description: Bump a project release version, synchronize version fields, create a release commit, add an annotated git tag, and push branch/tag to a remote. Use when the user asks to publish a new version or requests "bump version + commit + tag + push" release steps.
---

# Git Release Publish

## Overview

Execute a deterministic release flow: update version metadata, commit only release-related files, create a version tag, and push both commit and tag.

## Required Inputs

Collect and confirm these values before editing:

- Target version string (for example `1.1.0`)
- Tag format (default `v<version>`, for example `v1.1.0`)
- Target branch (usually current branch)
- Remote name (default `origin`)

## Workflow

1. Inspect repository state.
- Run `git status -sb`.
- Run `git tag -l <tag>` to verify the tag does not already exist.
- If tag already exists, stop and ask the user whether to reuse or choose a new version.

2. Locate version fields.
- Use `rg` to find version declarations (for example in `.csproj`, `package.json`, `pyproject.toml`, manifests).
- Update all relevant version fields consistently (assembly/file/informational version where applicable).

3. Validate build.
- Run the project build command after version edits.
- If build fails, stop and report errors before commit/tag.

4. Create release commit.
- Stage only release-relevant files (`git add <paths>`), avoid unrelated changes.
- Use commit message: `release: bump version to v<version>`.
- Do not amend existing commits unless explicitly requested.

5. Create annotated tag.
- Run `git tag -a v<version> -m "Release v<version>"`.

6. Push commit and tag.
- Push branch first, then tag:
  - `git push <remote> <branch>`
  - `git push <remote> v<version>`

7. Verify result.
- Run `git status -sb` to confirm sync.
- Run `git show --stat --oneline <new-commit>`.
- Run `git tag -n1 --list v<version>`.

## Guardrails

- Never include unrelated modified files in the release commit.
- Never delete or overwrite existing tags without explicit user approval.
- If remote push fails due to network/sandbox restrictions, retry with proper escalation.
- Report exact commit hash and tag after completion.
