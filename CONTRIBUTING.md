# Contributing

webspine is starting its MVP implementation. Read the charter and architecture before choosing a task. The MVP plan contains issue seeds; GitHub issues have not yet been created.

Use a short feature branch for a focused change. A pull request should explain the problem, resulting behavior, relevant validation and limitations. Link its issue when available.

Before submitting code, run `dotnet build Webspine.slnx`, `dotnet run --project tests/Webspine.Checks` and `dotnet run --project tests/Webspine.ManagementChecks`. These commands are the same on Windows, Linux and macOS. Add meaningful checks as behaviors are implemented. A three-platform GitHub workflow is included; remote execution remains pending repository creation.

An issue is complete when its acceptance criteria have evidence, relevant checks pass, and the API/operation documentation matches the implementation. Decisions that affect source ownership, permissions, storage or release guarantees belong in a short decision record.

Do not include credentials, runtime data or generated output in a commit or an issue. Report a security issue privately to the maintainer once a contact channel has been established; avoid posting credentials or exploit details in a public issue.
