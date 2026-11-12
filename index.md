# xat-workflow-orchestrator documentation

xat-workflow-orchestrator is a .NET 10 program. It runs pipelines for programs on the same machine. A pipeline gets document chunks from xat-rag, and it asks xat-llm-engine for the answer. The orchestrator listens on `http://127.0.0.1:8430` by default.

| Page | Read it for |
|---|---|
| [API reference](docs/api.md) | The routes, the pipeline files, the built-in steps, the settings and the limits. |
| [Quickstart](docs/quickstart.md) | Running a release, building from source, and making the first calls. |
| [Architecture](docs/architecture.md) | The layers, the ports and adapters, the run lifecycle and the extension points. |

## Build and test

The build needs the .NET SDK 10.0.401. The file `global.json` pins this version and turns off roll-forward. Run these commands from the root of the repository:

```bash
dotnet restore XatOrchestrator.slnx
dotnet build XatOrchestrator.slnx
dotnet test XatOrchestrator.slnx
```

If port 8430 is in use, set `ASPNETCORE_URLS=http://127.0.0.1:0` before you run the tests.

## Release

Push a tag in the form `vX.Y.Z` to start the release workflow. The workflow runs the tests, builds one archive for each platform, and publishes a GitHub release with `SHA256SUMS.txt`. A tag with a suffix, such as `v1.2.3-rc1`, does not start it. To run a release, see the [quickstart](docs/quickstart.md#run-a-release).
