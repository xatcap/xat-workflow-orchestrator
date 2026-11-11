# xat-workflow-orchestrator documentation

xat-workflow-orchestrator runs pipelines for programs on the same machine. A pipeline calls xat-rag to find document chunks. It calls xat-llm-engine to write the answer. The orchestrator is a .NET 10 program. It listens on `http://127.0.0.1:8430` by default.

## Documentation

| Page | What it covers |
|---|---|
| [API reference](docs/api.md) | The HTTP routes, the pipeline file format, the built-in steps, the settings and the limits. |
| [Quickstart](docs/quickstart.md) | How to run a release, build and test from source, start the orchestrator and make the first calls. |
| [Architecture](docs/architecture.md) | The layers, the ports and adapters, the run lifecycle and the extension points. |
| [How it works](docs/how-it-works.md) | A step-by-step guide to one request, from the route to the answer. |

## Build and test

The build needs the .NET SDK 10.0.401. The file `global.json` pins this version, and it turns off roll-forward. Check the SDK with `dotnet --version`. Run these commands from the root of the repository:

```bash
dotnet restore XatOrchestrator.slnx
dotnet build XatOrchestrator.slnx
dotnet test XatOrchestrator.slnx
```

The tests use the Microsoft.Testing.Platform runner. `global.json` sets it. The solution has five test projects: Domain, Application, Infrastructure, Api and Architecture. The Architecture tests check the rules between the layers.

CI runs the same steps in the Release configuration:

```bash
dotnet restore XatOrchestrator.slnx
dotnet build XatOrchestrator.slnx --configuration Release --no-restore
dotnet test XatOrchestrator.slnx --configuration Release --no-build
```

### Port for the Api tests

Some Api tests start a real Kestrel server. The test host asks Kestrel for port 0, so the system picks a free port. If a test run fails because port 8430 is in use, stop the process that uses that port. You can also set `ASPNETCORE_URLS=http://127.0.0.1:0`.

## Release files

- `.github/workflows/ci.yml` runs on every pull request. It restores, builds and tests the solution in the Release configuration, on Ubuntu.
- `.github/workflows/release.yml` runs when a tag in the form `vX.Y.Z` is pushed. A tag with a suffix, such as `v1.2.3-rc1`, does not start it. The workflow runs these jobs in order:
  1. It tests the solution. A release builds only from a commit that passes every test.
  2. It creates a draft GitHub release.
  3. It publishes one self-contained program for each platform: `osx-arm64` and `linux-x64` as `.tar.gz` archives, and `win-x64` as a `.zip` archive. Each archive holds the program, the settings, `pipelines/` and `prompts/`. The job starts the program from the archive, and it checks that the program lists the pipelines. Then it uploads the archive to the draft release.
  4. It writes `SHA256SUMS.txt`, uploads it, and publishes the release.

To run a release archive, see the [quickstart](docs/quickstart.md#run-a-release).
