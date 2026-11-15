# api

The platform's .NET (LTS) backend: ASP.NET Core controllers, and later EF Core and PostgreSQL. It owns all state and business logic.

## Layout

- `Platform.slnx`: the solution.
- `global.json`: pins the .NET 10 SDK and turns on the Microsoft.Testing.Platform runner for `dotnet test`.
- `Directory.Build.props`: shared settings (net10.0, nullable on, implicit usings, warnings as errors).
- `Directory.Packages.props`: central package management. Every NuGet version is pinned exactly here.
- `src/Platform.Api/`: the web host. Controllers live in feature folders (`Health/` for now).
- `src/Platform.Core/`: business logic and service interfaces. It never references an Azure or Stripe SDK.
- `tests/Platform.Api.Tests/`: xunit.v3 tests that run in process through `WebApplicationFactory`.

## Run locally

```sh
cd api
dotnet build Platform.slnx
dotnet test Platform.slnx
dotnet run --project src/Platform.Api      # http://127.0.0.1:5080/health -> {"status":"ok"}
```

`GET /health` is an unversioned, anonymous liveness check. Its body is exactly `{"status":"ok"}`, with no version, environment or host details. Business endpoints will start under `/v1`.
