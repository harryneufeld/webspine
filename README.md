# web**spine**

An open-source, self-hosted modular web engine with a built-in content source, replaceable CMS adapters, and controlled website releases. The product name is `webspine`; the wordmark is web**spine**, with “spine” bold. The repository folder is `webspine`.

## Current state

The local authoring workflow supports Start blank or Use demo, persistent page editing, revision conflicts, retained previews and content/image export. A separate SQLite content module implements the replaceable source contract. Validated prerendered builds and delivery support extension hooks, replaceable caching, HEAD and ETags. Accounts, dynamic rendering, durable jobs and publishing remain planned.

The earlier `website-poc` sibling folder is a separate experiment. Its source, generated credentials and runtime data are not part of this repository. Its successful checks are evidence about that experiment, not validation of the MVP.

## Project documents

- [Project charter](PROJECT.md): purpose, scope and product rules.
- [Architecture](docs/architecture.md): responsibilities and planned boundaries.
- [Content contract](docs/content-contract.md) and [extensions](docs/extensions.md): current contracts and modularity roadmap.
- [Delivery pipeline](docs/delivery.md): page modes, replaceable caching, HTTP behavior and current limits.
- [Local management](docs/management.md): setup, editing, stored data and preview limitations.
- [MVP plan](docs/mvp-plan.md): milestones and issue seeds with acceptance criteria.
- [Foundation decision](docs/decisions/0001-fresh-mvp-foundation.md): what this workspace establishes.
- [Validation](VALIDATION.md): checks performed in this workspace.
- [Contribution guide](CONTRIBUTING.md): development and review workflow.

## Develop locally

Requires a .NET 10 SDK compatible with `global.json`. From the repository root:

Windows, Linux and macOS are target platforms; Linux is the primary hosting target. Required commands use the .NET CLI and do not require PowerShell, Visual Studio or IIS. The GitHub workflow checks all three platforms; only Windows has been executed locally so far. See the repository's Actions runs for remote verification results.

```text
dotnet build Webspine.slnx
dotnet run --project tests/Webspine.Checks
dotnet run --project tests/Webspine.ManagementChecks
dotnet run --project src/Webspine.Management -- --urls http://127.0.0.1:9087
```

`GET http://127.0.0.1:9087/health` reports process health. Enable the optional demo with:

```text
dotnet run --project src/Webspine.Management -- --urls http://127.0.0.1:9087 --environment Development --Demo:Enabled true
```

Open `http://127.0.0.1:9087/demo/`. The demo is disabled by default, accepts only loopback requests and refuses startup outside Development when enabled. It renders a read-only fixture in memory, without initializing persistent customer data. Stop with Ctrl+C. The management checks above start temporary hosts, test configuration/routing and stop their own processes on completion.

The demo's internal memory cache is enabled by default. Append `--Demo:CacheEnabled false` to substitute the no-cache provider. Browser/proxy caching remains disabled for this development demo. Neither setting changes the generated page bytes.

## Next implementation

Try the local editor:

```text
dotnet run --project src/Webspine.Management -- --urls http://127.0.0.1:9087 --environment Development --Management:Enabled true
```

Open `http://127.0.0.1:9087/manage`. Choose a blank site or an explicit copy of the five-page demo. Save a draft, then Build preview. Restarting preserves the site and existing preview URLs. Management is disabled by default and restricted to Development and loopback access; it has no accounts yet. See [local management](docs/management.md) for storage settings and limits.

Next implement accounts and server permissions before enabling hosted management or AI access, followed by durable builds and exact-artifact publication. The [content decision](docs/decisions/0006-built-in-content-and-first-management-workflow.md) records why the first built-in engine uses a narrow C# module and SQLite.

The repository is [harryneufeld/webspine](https://github.com/harryneufeld/webspine), initially private while the MVP is developed. The [MVP plan](docs/mvp-plan.md) links the first four implementation issues; GitHub owns their status.
