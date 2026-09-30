# 0001: Start the MVP in a fresh workspace

Status: accepted foundation decision. Date: 27 September 2026.

## Context

The user selected a new `vibe-press` folder and requested a fresh MVP rather than pushing a repository named `website-poc`. The verified PoC is useful evidence for content/design separation and controlled publication, but contains demo authentication and file-backed workflow assumptions.

## Decision

- Establish the MVP repository root in `vibe-press` with VibePress project names.
- Carry forward the planning documents and update their claims and paths for the fresh workspace.
- Preserve the PoC separately; do not copy its application, tokens, runtime state or build output.
- Begin with one C# ASP.NET Core management project targeting .NET 10, using the same SDK baseline already verified locally. Add internal modules/projects when implemented responsibilities justify them.
- Include only a host health endpoint and an explicit foundation-status response initially. Do not present the scaffold as an implemented CMS.
- Keep storage, identity, content engine and external CMS choices open until the corresponding discovery tasks are complete.
- Initialize local Git for reviewable development. Remote creation, publishing and issue creation remain pending repository owner/visibility decisions.

## Consequences

The MVP starts without demo behavior or migrated customer data. Proven requirements must receive fresh implementation and tests here. The PoC's successful checks do not count as MVP checks. Build workers and public delivery remain separate logical responsibilities and will be added when their milestones are implemented.
