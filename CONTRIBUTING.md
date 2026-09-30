# Contributing

webspine is starting its MVP implementation. Read the charter and architecture before choosing a task. The MVP plan links implementation issues and retains broader issue seeds; GitHub owns task status.

Use a short feature branch for a focused change. A pull request should explain the problem, resulting behavior, relevant validation and limitations. Link its issue when available.

Before submitting code, run `dotnet build Webspine.slnx`, `dotnet run --project tests/Webspine.Checks` and `dotnet run --project tests/Webspine.ManagementChecks`. These commands are the same on Windows, Linux and macOS. Add meaningful checks as behaviors are implemented. The three-platform workflow runs in [GitHub Actions](https://github.com/harryneufeld/webspine/actions); inspect its results before merging changes.

An issue is complete when its acceptance criteria have evidence, relevant checks pass, and the API/operation documentation matches the implementation. Decisions that affect source ownership, permissions, storage or release guarantees belong in a short decision record.

Do not include credentials, runtime data or generated output in a commit or an issue. Report a security issue privately to the maintainer once a contact channel has been established; avoid posting credentials or exploit details in a public issue.
