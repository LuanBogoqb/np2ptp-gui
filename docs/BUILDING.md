# Building from Source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet build Np2ptpGui.sln
dotnet test Np2ptpGui.sln
```

`src/Np2ptpGui` is the app itself. `tests/Np2ptpGui.Tests` covers the pieces that don't need a real np2ptp binary to test — config parsing, the task manager, process handling, and so on.

Releases are built and signed automatically by the `Release` GitHub Actions workflow on every `vX.X.X` tag push; see `.github/workflows/release.yml` if you're setting up your own fork's release pipeline.
