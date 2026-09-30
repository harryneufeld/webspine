# web**spine**

An open-source, self-hosted modular web engine with a built-in content source, replaceable CMS adapters, and controlled website releases. The product name is `webspine`; the wordmark is web**spine**, with “spine” bold. The repository folder is `webspine`.

## Current state

The first discovery slices implement immutable content, a replaceable source contract, validated prerendered builds and extension points for validators, renderers and export contributors. An optional fictional five-page demo now runs through a delivery module with a replaceable memory-cache package, delivery header hooks, HEAD and ETag support. Persistent editing, dynamic rendering, identity, durable jobs and publishing remain planned.

The earlier `website-poc` sibling folder is a separate experiment. Its source, generated credentials and runtime data are not part of this repository. Its successful checks are evidence about that experiment, not validation of the MVP.

## Project documents

- [Project charter](PROJECT.md): purpose, scope and product rules.
- [Architecture](docs/architecture.md): responsibilities and planned boundaries.
- [Content contract](docs/content-contract.md) and [extensions](docs/extensions.md): current contracts and modularity roadmap.
- [Delivery pipeline](docs/delivery.md): page modes, replaceable caching, HTTP behavior and current limits.
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

Continue MVP-01 by comparing a narrow C# content store with maintained CMS engines against this five-page fixture. Record the default CMS decision, then select storage and identity before implementing editing. The current model is a tested starting point; CMS discovery remains open.

The repository is [harryneufeld/webspine](https://github.com/harryneufeld/webspine), initially private while the MVP is developed. The MVP plan still contains issue seeds; GitHub issues have not yet been created.
