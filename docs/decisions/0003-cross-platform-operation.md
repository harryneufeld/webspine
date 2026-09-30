# 0003: Cross-platform operation with Linux-first hosting

Accepted requirement, 30 September 2026.

The user requires OS-independent operation, especially Linux for hosting, and identified the PowerShell-only check as a portability issue.

Keep C# with modern .NET and ASP.NET Core. Windows, Linux and macOS are target platforms; Linux is the primary production hosting target. Required developer and verification operations use portable .NET commands. Do not require IIS, Windows APIs, Visual Studio, PowerShell or Windows-specific storage/image components. Choose future dependencies against these constraints.

Replace the management verification script with a C# console runner. Add the same build/content/management checks to a GitHub matrix for all three operating systems. Native platform runs must pass before claiming validated support. Record concrete OS versions and architectures for the eventual supported deployment profile; OS-independent does not mean every historical OS version or hosting plan.

Alternative: change backend language. That would not remove platform-specific dependency or tooling risks and provides no portability benefit by itself. Retaining C# preserves existing knowledge and typed module contracts while addressing the actual tooling issue.

Linux container packaging remains an MVP operation task. Management hosting requires a .NET-capable process/container environment; generated static website files remain independently deployable. PHP-only shared hosting is not an assumed management target.

Reference: [official .NET 10 supported operating systems](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).
