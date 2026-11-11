# Quickstart

This page shows how to build, test and start xat-workflow-orchestrator. It also shows the first calls and how to extend the orchestrator. The routes and the settings are in the [API reference](api.md). The design is in the [architecture](architecture.md).

The commands run in a POSIX shell. Run them from the root of the repository, unless a step says otherwise.

## Prerequisites

- The .NET SDK 10.0.401. The file `global.json` pins this version and disables roll-forward. Check the version with `dotnet --version`. The output must be `10.0.401`.
- xat-rag and xat-llm-engine, each running on a loopback address. Read the documentation of each service to start it.
- Python 3, for the example receiver in [Run in the background](#run-in-the-background) and for the example in [Call the route with the OpenAI Python library](#call-the-route-with-the-openai-python-library).

If the SDK is installed in `~/.dotnet`, add it to the path before you run `dotnet`:

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
```

## Build

```bash
dotnet build XatOrchestrator.slnx
```

The program is in `src/Xat.Orchestrator.Api/bin/Debug/net10.0/`. The build copies the files of `pipelines/` and `prompts/` into the same folder.

## Test

```bash
dotnet test XatOrchestrator.slnx
```

All tests pass in this repository. The tests start the real pipelines with fake upstream services, so they need no running xat-rag or xat-llm-engine.

## Start the upstream services

Start xat-rag and xat-llm-engine. The orchestrator uses these default addresses:

| Service | Default address | Setting |
|---|---|---|
| xat-rag | `http://127.0.0.1:8421` | `Upstreams:XatRag:BaseUrl` |
| xat-llm-engine | `http://127.0.0.1:8420` | `Upstreams:XatLlmEngine:BaseUrl` |

If a service uses another port, change its address. You can pass the address on the command line, as shown in the next step.

Some services choose a new port at each start. For these services, use a ready file. Set `BaseUrl` to an empty value, and set `ReadyFile` to the path of the file. The file must hold a JSON object with the key `url`:

```json
"XatRag": { "BaseUrl": "", "ReadyFile": "/path/to/ready.json", "RequestTimeoutSeconds": 30, "RetryCount": 2 }
```

The orchestrator reads the file again after a failed connection.

## Start the orchestrator

Start the orchestrator from the root of the repository:

```bash
dotnet run --project src/Xat.Orchestrator.Api -- \
  --Upstreams:XatRag:BaseUrl=http://127.0.0.1:8421 \
  --Upstreams:XatLlmEngine:BaseUrl=http://127.0.0.1:8420
```

The options after `--` override the settings in `appsettings.json`. The output shows the listen address when the start succeeds:

```text
Now listening on: http://127.0.0.1:8430
Application started. Press Ctrl+C to shut down.
```

To stop the orchestrator, press Ctrl+C.

The orchestrator listens on `127.0.0.1:8430` by default. It refuses to start on any other address. See [Security](api.md#security).

## First calls

Check that the orchestrator runs and that the upstream services are ready:

```bash
curl -s http://127.0.0.1:8430/health/ready
```

The status is `ready` when both upstream services answer. The status is `degraded` when one of them is down. The orchestrator still runs in that case.

List the pipelines:

```bash
curl -s http://127.0.0.1:8430/v1/pipelines
```

Run a pipeline and wait for the complete answer. This call needs both upstream services:

```bash
curl -s -X POST http://127.0.0.1:8430/v1/ask \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Which port does the orders service use?"}}'
```

Run the same question as a stream. The `-N` option stops curl from buffering the events:

```bash
curl -N -X POST http://127.0.0.1:8430/v1/pipelines/rag-qa/runs \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Which port does the orders service use?"}, "stream": true}'
```

Each event has an `event:` line and a `data:` line. The last event is `run.completed` or `run.error`. The [streamed runs](api.md#streamed-runs) section lists all events.

## Run in the background

An asynchronous run returns a run ID at once. The orchestrator then sends the result to a webhook. This section starts such a run, and it sends the result to a small receiver. The run needs xat-rag and xat-llm-engine running.

1. Save this file as `receiver.py` in any folder:

    ```python
    import json
    from http.server import BaseHTTPRequestHandler, HTTPServer

    class Receiver(BaseHTTPRequestHandler):
        def do_POST(self):
            body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            print("Run ID:", self.headers["X-Run-Id"])
            print(json.dumps(body, indent=2))
            self.send_response(200)
            self.send_header("Content-Length", "0")
            self.end_headers()

        def log_message(self, format, *args):
            pass

    HTTPServer(("127.0.0.1", 9000), Receiver).serve_forever()
    ```

2. Open a second terminal. Start the receiver in the folder that holds the file:

    ```bash
    python3 receiver.py
    ```

   The receiver listens on `127.0.0.1:9000`. It answers every request with status 200. It handles one request at a time, so use it for tests only.

3. Submit a run with the address of the receiver in the `webhook` field:

    ```bash
    curl -s -X POST 'http://127.0.0.1:8430/v1/pipelines/rag-qa/runs?mode=async' \
      -H "Content-Type: application/json" \
      -d '{"input": {"prompt": "Which port does the orders service use?"}, "webhook": "http://127.0.0.1:9000/hook"}'
    ```

   The orchestrator answers with 202 and the ID of the run. This output comes from a test run:

    ```json
    {"run_id":"run_6b60384bda74497db576","status":"queued","status_url":"/v1/runs/run_6b60384bda74497db576"}
    ```

4. Read the output of the receiver. The receiver prints the run ID and the body. The output below is abbreviated:

    ```text
    Run ID: run_6b60384bda74497db576
    {
      "event": "run.finished",
      "run": {
        "run_id": "run_6b60384bda74497db576",
        "pipeline": "rag-qa",
        "status": "succeeded",
        ...
        "webhook": {
          "url": "http://127.0.0.1:9000",
          "status": "pending",
          "attempts": 0,
          "error": null
        }
      }
    }
    ```

5. Read the run to see the delivery state:

    ```bash
    curl -s http://127.0.0.1:8430/v1/runs/run_6b60384bda74497db576
    ```

   The `webhook` field shows `"status": "delivered"` when the receiver accepts the body. If the receiver is not running, the delivery fails. A failed delivery does not change the result. See [Delivery state](api.md#delivery-state).

To stop the receiver, press Ctrl+C in its terminal.

## Call a pipeline with the OpenAI format

The route `POST /v1/chat/completions` accepts the OpenAI chat format. The model name is `pipeline/<name>`. The [API reference](api.md#openai-compatible-api) describes the route.

This example calls the `rag-qa` pipeline with curl. It needs xat-rag and xat-llm-engine running:

```bash
curl -s -X POST http://127.0.0.1:8430/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model": "pipeline/rag-qa", "messages": [{"role": "user", "content": "Which port does the orders service use?"}]}'
```

The answer is an OpenAI `chat.completion` object. The text is in `choices[0].message.content`. The run ID is in `xat.run_id`.

### Call the route with the OpenAI Python library

The steps below use the official OpenAI library for Python. The tested package is `openai`. Install it in a virtual environment, so that it stays apart from other Python programs.

1. Create the virtual environment in the folder of your project:

    ```bash
    python3 -m venv .venv
    ```

2. Install the package in the environment:

    ```bash
    .venv/bin/pip install openai
    ```

3. Save this file as `ask.py`:

    ```python
    from openai import OpenAI

    client = OpenAI(base_url="http://127.0.0.1:8430/v1", api_key="anything")

    completion = client.chat.completions.create(
        model="pipeline/rag-qa",
        messages=[{"role": "user", "content": "Which port does the orders service use?"}],
    )
    print(completion.choices[0].message.content)
    ```

4. Run the file. The orchestrator does not check the API key, so any value works:

    ```bash
    .venv/bin/python ask.py
    ```

The output is the answer of the pipeline. This output needs xat-rag and xat-llm-engine running:

```text
The orders service uses port 8080 [1].
```

## Upload documents to a RAG

Warning: The ingestion routes have no authentication. Any program on the same machine can upload, delete documents and rebuild a RAG. Read [Ingestion proxy](api.md#ingestion-proxy) before you use these routes.

This example uploads the file `orders.md` to the RAG `docs`. It needs xat-rag running.

1. Create the file `orders.md` in the current folder. Use any Markdown text.
2. Upload the file. The `documents` part lists the document IDs, and the `f1` part holds the file:

    ```bash
    curl -s -X PUT http://127.0.0.1:8430/v1/rags/docs/documents \
      -F 'documents=[{"doc_id":"docs:orders.md","kind":"markdown","part":"f1"}];type=application/json' \
      -F "f1=@orders.md;filename=orders.md"
    ```

   The answer is `202 Accepted` with a JSON body that holds the `job_id`. The job ID comes from xat-rag:

    ```json
    {"job_id":"<job ID>"}
    ```

3. Read the job. Replace `<job ID>` with the value from step 2:

    ```bash
    curl -s http://127.0.0.1:8430/v1/jobs/<job ID>
    ```

   The job route reads an ingestion job of xat-rag. It does not read an asynchronous run. To read a run, use `GET /v1/runs/{id}`. See [Jobs and asynchronous runs](api.md#jobs-and-asynchronous-runs).

The upload limit is 64 MiB by default. xat-rag has the same limit. See [Uploads](api.md#uploads).

## Allow another webhook host

The orchestrator sends webhooks only to loopback hosts by default. Use this procedure to allow another host.

Warning: the value `*` allows every host. A caller can then send requests to any address that the machine can reach. Use `*` only in a test.

1. Open `src/Xat.Orchestrator.Api/appsettings.json`.
2. Add the host name to the `AllowedHosts` list in the `Webhooks` section. Use only the host name, without a port or a path:

    ```json
    "AllowedHosts": [ "hooks.example.com" ]
    ```

3. Restart the orchestrator.
4. Submit a run with a webhook to a host that the list does not hold. The orchestrator answers with 400:

    ```json
    {"error":{"type":"invalid_request_error","message":"The webhook host is not allowed. An operator can allow it with Webhooks:AllowedHosts.","code":"webhook.host_not_allowed","run_id":null}}
    ```

You can also pass the host on the command line, for example `--Webhooks:AllowedHosts:0=hooks.example.com`. The [API reference](api.md#webhooks) lists the address rule.

## Add a pipeline

A pipeline is a YAML file. A new file is a new pipeline after a restart. No code change is needed.

1. Create the file `pipelines/docs-qa.yaml`. Use this content:

    ```yaml
    name: docs-qa
    version: 1
    summary: Answers a question from the docs context.
    stream: allowed
    input:
      type: object
      required: [prompt]
      additionalProperties: false
      properties:
        prompt:
          type: string
          minLength: 1
          maxLength: 20000
    steps:
      - id: system
        uses: prompt.system
        with:
          text: Answer from the sources and cite them.
      - id: user
        uses: prompt.user
        with:
          text: "${input.prompt}"
      - id: docs
        uses: rag.retrieve
        with:
          profile: docs
      - id: assemble
        uses: prompt.assemble
      - id: generate
        uses: llm.generate
      - id: citations
        uses: answer.verify-citations
    ```

2. Build the project again. The build copies the file to the output folder.

    ```bash
    dotnet build src/Xat.Orchestrator.Api
    ```

3. Restart the orchestrator.
4. Check that the new pipeline is listed:

    ```bash
    curl -s http://127.0.0.1:8430/v1/pipelines/docs-qa
    ```

The start checks the file. A file with a mistake stops the start, and the message names the file. See [Troubleshooting](#troubleshooting). The rules for pipeline files are in the [API reference](api.md#pipeline-files).

## Add a retrieval profile

A retrieval profile is an alias for a xat-rag context. Pipelines use the alias. Callers see the alias in `GET /v1/contexts`.

1. Open `src/Xat.Orchestrator.Api/appsettings.json`.
2. Add the alias under `Retrieval:Profiles`:

    ```json
    "api": { "Context": "ask-api", "Chunks": 5 }
    ```

3. Restart the orchestrator.
4. Check the alias:

    ```bash
    curl -s http://127.0.0.1:8430/v1/contexts
    ```

The `available` value is `true` when xat-rag lists the context. It is `false` when xat-rag does not list it, and `null` when xat-rag is not reachable. The context must exist in xat-rag. The orchestrator does not create it.

## Add a model alias

A model alias names a model of xat-llm-engine, with its limits.

1. Open `src/Xat.Orchestrator.Api/appsettings.json`.
2. Add the alias under `Llm:Models`. Use this example:

    ```json
    "small": {
      "Model": "local/small-model",
      "ContextWindow": 4096,
      "Tokenizer": "estimate",
      "Parallel": 1,
      "SupportsGrammar": false,
      "AllowedFields": [ "temperature", "max_tokens" ]
    }
    ```

3. Restart the orchestrator.
4. Check the alias:

    ```bash
    curl -s http://127.0.0.1:8430/v1/models
    ```

The `Model` value must have the form `provider/model`. The `Tokenizer` value must be `estimate`. The adapter sends only the request fields in `AllowedFields`. A step that sets `temperature` sends it only when this list has `temperature`.

To use the alias, set the `model` option of a step, for example `llm.generate`:

```yaml
  - id: generate
    uses: llm.generate
    with:
      model: small
```

## Add a prompt template

A prompt template is a Liquid file. A step uses it as text for a system prompt or an extra section. The template gets one value, named `input`. It holds the input of the run.

1. Create the file `prompts/brief@1.liquid`. The file name is `name@version.liquid`. Use this content:

    ```text
    Use short sentences.
    ```

2. Build the project again. The build copies the file to the output folder.
3. Add a `prompt.append` step before `prompt.assemble` in a pipeline:

    ```yaml
      - id: brief
        uses: prompt.append
        with:
          template: brief@1
    ```

4. Restart the orchestrator.

A template that is missing stops the start. The message names the pipeline, the step and the template. This is the real message for a missing template:

```text
Pipeline 'docs-qa': step 'brief': The prompt template 'brief@1' does not exist. A template file is named name@version.liquid.
```

A template writes its text as trusted text. The assemble step does not escape it. Do not write free text from the caller into a template. Use an enum in the input schema of the pipeline, as the `language` input of `custom-prompt` does.

## Add a step in C#

Use a step when no built-in step does the job. A step is a class that implements `IPipelineStep<TOptions>`. The options class holds the values from the `with` block of the step.

1. Create the options class and the step class. Put each class in its own file in `src/Xat.Orchestrator.Application/Steps/`. The file `TextSuffixOptions.cs` holds this class:

    ```csharp
    namespace Xat.Orchestrator.Application.Steps;

    /// <summary>The options of <see cref="TextSuffixStep"/>.</summary>
    public sealed class TextSuffixOptions
    {
        public string? Suffix { get; init; }
    }
    ```

    The file `TextSuffixStep.cs` holds this class:

    ```csharp
    using Xat.Orchestrator.Application.Engine;

    namespace Xat.Orchestrator.Application.Steps;

    /// <summary>Adds a suffix to the prompt of the caller.</summary>
    public sealed class TextSuffixStep : IPipelineStep<TextSuffixOptions>
    {
        public ValueTask<StepOutcome> ExecuteAsync(StepContext context, TextSuffixOptions options, CancellationToken ct)
        {
            context.Run.State.Prompt.User += options.Suffix;
            return ValueTask.FromResult(StepOutcome.Continue);
        }
    }
    ```

2. Register the step in the `AddApplication` method of `src/Xat.Orchestrator.Application/DependencyInjection.cs`. Add it to the chain of `AddPipelineStep` calls:

    ```csharp
    .AddPipelineStep<TextSuffixStep, TextSuffixOptions>(
        "text.suffix", StepTraits.None, "Adds a suffix to the prompt of the caller.")
    ```

3. Build the solution, and run the tests.
4. Restart the orchestrator. The new step appears in `GET /v1/steps`.

A pipeline then uses the step by its name:

```yaml
  - id: suffix
    uses: text.suffix
    with:
      suffix: " Answer briefly."
```

The step can also return `StepOutcome.Complete` to end the run, or `StepOutcome.Fail` to stop with an error. The [architecture](architecture.md#extensibility) page lists the other extension points, such as middleware and hooks.

## Troubleshooting

The messages below come from the code or from a test run. The start messages go to the error output, and the start stops with exit code 1.

| Message or symptom | Cause | Action |
|---|---|---|
| `Pipeline 'bad': step 'not-a-step' uses the unknown step type 'not.a.step'.` | A step type name is wrong. The start stops. | Fix the `uses` value. The names are in [Built-in steps](api.md#built-in-steps). |
| `Pipeline 'docs-qa': step 'brief': The prompt template 'brief@1' does not exist. A template file is named name@version.liquid.` | The template file is missing, or the project was not built after the file was added. The start stops. | Create the file, build the project, and restart. |
| `Pipeline 'docs-qa': step 'docs': The retrieval profile 'api' is not configured. Add it to Retrieval:Profiles.` | A `profile` option names an alias that does not exist. The start stops. | Add the alias to `Retrieval:Profiles`, or change the option. |
| `The orchestrator has no authentication, so it must listen on a loopback address only. Use 127.0.0.1, [::1] or localhost.` | A listen address is not loopback. The start stops. | Set `urls`, `--urls` or `ASPNETCORE_URLS` to a loopback address. |
| `The pipeline folder 'x' does not exist.` | The `Pipelines:Folder` path is wrong. The start stops. | Fix the path. A relative path starts at the folder of the program. |
| `Api:DefaultPipeline names the pipeline 'x', which does not exist.` | The default pipeline is not loaded. The start stops. | Set `Api:DefaultPipeline` to an existing pipeline name. |
| 503 with code `xat-rag.unreachable` and the message `xat-rag is not reachable.` | xat-rag is not running, or the address is wrong. | Start xat-rag. Check `Upstreams:XatRag:BaseUrl` or `ReadyFile`. |
| 503 with code `xat-llm-engine.unreachable` and the message `xat-llm-engine is not reachable.` | xat-llm-engine is not running, or the address is wrong. | Start xat-llm-engine. Check `Upstreams:XatLlmEngine:BaseUrl`. |
| `GET /health/ready` returns `"status":"degraded"` | An upstream service is down. The orchestrator still answers with status 200. | Start the upstream service. |
| 503 with code `admission.full` and `Retry-After: 1` | All run slots and queue places are in use. | Wait, and send the request again. Change `Api:MaxConcurrentRuns` or `Api:QueueLimit` only if the upstream services can take more work. |
| 400 with code `prompt.too_large` and the message `The fixed parts of the prompt do not fit into the context window of the model.` | The prompt, the system prompt and the template are larger than the context window, minus `reserveOutput`. | Shorten the prompt. Use a model with a larger `ContextWindow`, or lower `reserveOutput`. |
| 400 with code `input.invalid` and the message `input.prompt: the property is required.` | The input has no `prompt` key. | Send the key. The message names the path and the rule. |
| 400 with code `request.content_type` and the message `The content type must be application/json.` | The POST request has another content type. | Set the header `Content-Type: application/json`. |
| 400 with code `request.invalid_json` or `request.empty` | The body is not JSON, or it is empty. | Send a JSON object. |
| 413 with code `request.too_large` | The body is larger than `Api:MaxRequestBodyBytes`. | Send less data, or raise the limit. |
| 400 with code `stream.not_supported` | The pipeline has `stream: never`, and the request sets `stream` to `true`. | Set `stream` to `false`. |
| 400 with code `retrieve.profile_unknown` | The `context` input names an alias that does not exist. | Use an alias from `GET /v1/contexts`. |
| 404 with code `pipeline.not_found` | The name is wrong, or the file was added after the last start. | Check `GET /v1/pipelines`, and restart after you add a file. |
| 400 with an HTML page that says `Invalid Hostname` | The `Host` header is not `localhost`, `127.0.0.1` or `[::1]`. | Use one of these names in the URL. |
| 504 with code `run.timeout` | The run passed `Runs:RunTimeout`. | Shorten the pipeline, or raise the limit, up to 1 hour. |
| 500 with code `internal_error` | A failure inside the orchestrator. The body has no details. | Read the log of the orchestrator, which has the exception. |
| 400 with code `webhook.host_not_allowed` | The webhook host is not a loopback host, and it is not in `Webhooks:AllowedHosts`. | Add the host name to `Webhooks:AllowedHosts`, and restart. Or use a loopback host, such as `127.0.0.1`. |
| 404 with code `run.not_found` after a restart | The runs are in memory, so a restart removes them. | Submit the run again. Keep the result from the webhook or from the `GET` response. |
| 404 with code `run.not_found` after some time | The store removed the finished run. `Jobs:RetentionMinutes` or `Jobs:RetainFinished` was reached. | Read the run sooner, or raise the limit. |
| 503 with code `jobs.queue_full` and `Retry-After: 5` | All workers are busy, and the queue of waiting runs is full. | Wait 5 seconds, and submit again. Raise `Jobs:Workers` only if the upstream services can take more work. |
| `"status": "failed"` in the `webhook` field of a run | The receiver is not running, refused the request, or did not answer in time. The `error` field tells why. | Start the receiver, and check the address in `webhook`. The run result does not change, so read it from `result`. |
| 400 with code `request.model_invalid` | The `model` value does not start with `pipeline/`. For example, the value is `gpt-4o`. | Use the form `pipeline/<name>`. Take the name from `GET /v1/models`. |
| 404 with code `model.not_found` | The name after `pipeline/` is not a pipeline, or the pipeline file was added after the last start. | Check the names in `GET /v1/models`. Restart the orchestrator after you add a file. |
| 400 with code `request.pipeline_not_chat` | The pipeline has no input named `prompt`, so the OpenAI-compatible route cannot call it. | Call the pipeline with `POST /v1/pipelines/{name}/runs`. Or add an input named `prompt` to the pipeline. |
| 404 with code `ingestion.route_disabled` | The operator turned off the route in `Ingestion:DisabledRoutes`, or `Ingestion:Enabled` is `false`. | Remove the route name from the list, and restart. The names are in [Route names and paths](api.md#route-names-and-paths). |
| 404 with code `ingestion.route_not_found` | The path ends with a slash, or the document ID has a dot segment, such as `..`. xat-rag gets no request. | Remove the slash at the end of the path, or use a document ID without a dot segment. |
| 403 with code `request.origin_not_allowed` | The request has an `Origin` header. A browser adds this header when a web page makes the request. The orchestrator refuses it on every route. | Call the orchestrator from a program, such as `curl` or a server-side client. A browser cannot call the orchestrator. |
| 413 with code `request.too_large` and the message `The upload is larger than the limit.` | The upload is larger than `Ingestion:MaxUploadBytes`. The default is 64 MiB. | Split the upload. xat-rag refuses an upload above 64 MiB, so a higher limit does not help. |
| 503 with code `xat-rag.unreachable` on an ingestion route, and `Retry-After: 5` | xat-rag is not running, or the address is wrong. The ingestion routes do not retry. | Start xat-rag. Check `Upstreams:XatRag:BaseUrl` or `ReadyFile`. Wait 5 seconds, and send the request again. |
