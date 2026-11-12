# Quickstart

This page shows how to make a first successful call to xat-workflow-orchestrator. The [API reference](api.md) lists the routes and the settings. The [architecture](architecture.md) page describes the code.

## Before you start

- A release needs no .NET SDK.
- A source build needs the .NET SDK 10.0.401. The file `global.json` pins this version. Check it with `dotnet --version`. If the SDK is in `~/.dotnet`, add it to the path:

    ```bash
    export PATH="$HOME/.dotnet:$PATH"
    export DOTNET_ROOT="$HOME/.dotnet"
    ```

- xat-rag and xat-llm-engine must run on a loopback address. Read the documentation of each service to start it.

## Run a release

1. Download the archive for your platform from the Releases page of the repository:
    - `xat-orchestrator-vX.Y.Z-osx-arm64.tar.gz` for macOS on Apple silicon.
    - `xat-orchestrator-vX.Y.Z-linux-x64.tar.gz` for Linux on x64.
    - `xat-orchestrator-vX.Y.Z-win-x64.zip` for Windows on x64.
2. Unpack the archive. On macOS and Linux, run `tar -xzf` with the archive name. On Windows, extract the zip file.
3. On macOS only, remove the quarantine flag. The program is not signed, so macOS can block it:

    ```bash
    xattr -dr com.apple.quarantine xat-orchestrator-vX.Y.Z-osx-arm64
    ```

4. Go to the unpacked folder and start the program. Run `./Xat.Orchestrator.Api` on macOS and Linux, or `Xat.Orchestrator.Api.exe` on Windows.

The program reads its settings, `pipelines/` and `prompts/` from its own folder. The output shows `Now listening on: http://127.0.0.1:8430` when the start succeeds. Press Ctrl+C to stop the program.

## Build and test from source

Run these commands from the root of the repository:

```bash
dotnet build XatOrchestrator.slnx
dotnet test XatOrchestrator.slnx
```

The build puts the program in `src/Xat.Orchestrator.Api/bin/Debug/net10.0/`. It also copies `pipelines/` and `prompts/` into that folder. The tests use fake upstream adapters, so they need no running xat-rag or xat-llm-engine.

## Start from source

```bash
dotnet run --project src/Xat.Orchestrator.Api
```

Run this command from the root of the repository. The output shows `Now listening on: http://127.0.0.1:8430` when the start succeeds. Press Ctrl+C to stop the orchestrator.

The orchestrator listens on `127.0.0.1:8430` by default. To change the port, pass a new loopback address, for example `--urls http://127.0.0.1:8431`. The orchestrator refuses any address that is not loopback.

## Configure the upstream services

The defaults are `http://127.0.0.1:8421` for xat-rag and `http://127.0.0.1:8420` for xat-llm-engine. If a service uses another port, pass its address on the command line:

```bash
dotnet run --project src/Xat.Orchestrator.Api -- \
  --Upstreams:XatRag:BaseUrl=http://127.0.0.1:9421 \
  --Upstreams:XatLlmEngine:BaseUrl=http://127.0.0.1:9420
```

For a release, add the same options after `Xat.Orchestrator.Api`. You can also change the values in `appsettings.json`.

If a service picks a new port at each start, use a ready file instead. In the settings file, set `BaseUrl` to an empty value, and set `ReadyFile` to the path of the file. These keys sit under `Upstreams`:

```json
"XatRag": { "BaseUrl": "", "ReadyFile": "/path/to/ready.json" }
```

The file holds a JSON object with the key `url`.

See [Upstream services](api.md#upstream-services) for all upstream settings.

## Check the orchestrator

```bash
curl -s http://127.0.0.1:8430/health/ready
```

The status is `ready` when both services answer. The status is `degraded` when one service is down. The orchestrator still runs in that case.

## First sync call

This call waits for the complete answer. It uses the default pipeline, `rag-qa`:

```bash
curl -s -X POST http://127.0.0.1:8430/v1/ask \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Which port does the orders service use?"}}'
```

The response holds the `run_id`, the `status` and the `answer`. The `answer.citations` list names each source. A number such as `[1]` in the text matches the `number` of a citation.

## First streaming call

The `-N` option stops curl from buffering the events:

```bash
curl -N -X POST http://127.0.0.1:8430/v1/pipelines/rag-qa/runs \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Which port does the orders service use?"}, "stream": true}'
```

Each event has an `event:` line and a `data:` line. The stream ends with `run.completed` or `run.error`. See [Streamed runs](api.md#streamed-runs) for the events.

## First async call with a webhook

An async run returns a run ID at once. The orchestrator sends the result to a webhook when the run ends.

1. Start a receiver on `127.0.0.1:9000`. Any local HTTP server that answers a `POST` with status 200 works for a test.
2. Submit the run with the address of the receiver:

    ```bash
    curl -s -X POST 'http://127.0.0.1:8430/v1/pipelines/rag-qa/runs?mode=async' \
      -H "Content-Type: application/json" \
      -d '{"input": {"prompt": "Which port does the orders service use?"}, "webhook": "http://127.0.0.1:9000/hook"}'
    ```

   The response has status 202, the `run_id` and the `status_url`.

3. Read the run. Replace `<run_id>` with the ID from step 2:

    ```bash
    curl -s http://127.0.0.1:8430/v1/runs/<run_id>
    ```

   The field `webhook.status` is `delivered` when the receiver accepted the webhook. A failed delivery does not change the result of the run. See [Webhooks](api.md#webhooks) for the delivery rules.

## Add a pipeline

A pipeline is a YAML file. A new file is a new pipeline after a restart.

1. Copy `pipelines/rag-qa.yaml` to `pipelines/docs-qa.yaml`. Change the `name` key to `docs-qa`. The file name does not set the name of the pipeline.
2. For a source build, build the project again so that the file is copied:

    ```bash
    dotnet build src/Xat.Orchestrator.Api
    ```

   For a release, put the file in the `pipelines` folder next to the program.

3. Restart the orchestrator.
4. Check that the pipeline is listed:

    ```bash
    curl -s http://127.0.0.1:8430/v1/pipelines/docs-qa
    ```

A file with a mistake stops the start, and the message names the file. The rules for pipeline files are in [Pipeline files](api.md#pipeline-files).

To add a retrieval alias, a model alias or a webhook host, change the settings and restart. See [Settings](api.md#settings) and [Webhooks](api.md#webhooks).

## Troubleshooting

The messages below come from the code. A start message goes to the error output, and the start stops with exit code 1.

| Message or symptom | Cause | Action |
|---|---|---|
| `The orchestrator has no authentication, so it must listen on a loopback address only. Use 127.0.0.1, [::1] or localhost.` | The listen address is not loopback. The start stops. | Set `urls`, `--urls` or `ASPNETCORE_URLS` to a loopback address. |
| Message that starts with `Upstreams:XatRag:BaseUrl must be an http or https loopback address with no path` | The address has a path, a query, or it is not a loopback address. The start stops. | Remove the path. Use a loopback address, such as `http://127.0.0.1:8421`. |
| 503 with code `xat-rag.unreachable` or `xat-llm-engine.unreachable`, and `Retry-After` | The service is not running, or the address is wrong. `/health/ready` shows `degraded`. | Start the service. Check its `BaseUrl`, or its ready file. |
| 400 with code `request.content_type` | The POST request has no `application/json` content type. The `curl -d` option sends a form type by default. | Add the header `-H "Content-Type: application/json"`. |
| 400 with an HTML page that says `Invalid Hostname` | The `Host` header is not `localhost`, `127.0.0.1` or `[::1]`. | Use one of these names in the URL. |
| 500 with code `internal.error` or `run.internal_error` | An unexpected error occurred. The body has no details. | Read the log of the orchestrator. It holds the exception. |

Other errors are in the [error table](api.md#errors).

## See also

- [API reference](api.md) lists the routes, the pipeline files, the steps and the settings.
- [Architecture](architecture.md) describes the layers and the extension points.
