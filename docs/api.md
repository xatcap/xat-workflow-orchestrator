# API reference

This page describes the HTTP routes, the pipeline file format, the built-in steps, the settings and the limits. Read the [quickstart](quickstart.md) to start the orchestrator. Read the [architecture](architecture.md) to learn how the code is built.

- [Conventions](#conventions)
- [Security](#security)
- [Errors](#errors)
- [Routes](#routes)
- [Pipeline files](#pipeline-files)
- [Built-in steps](#built-in-steps)
- [Expressions](#expressions)
- [Input schema](#input-schema)
- [Shipped pipelines](#shipped-pipelines)
- [Settings](#settings)
- [Limits](#limits)

## Conventions

- The default address is `http://127.0.0.1:8430`. The `Urls` setting changes it. Use a loopback address only.
- Bodies use JSON in UTF-8. Field names use snake_case, for example `run_id`.
- A POST body must have the content type `application/json`. Otherwise the orchestrator returns `request.content_type`. The ingestion routes send the body to xat-rag as it is.
- The `Host` header must name `localhost`, `127.0.0.1` or `[::1]`, with or without a port. Another host gets a 400 HTML page, not an error body.
- Pipeline names, retrieval aliases and model aliases match `[a-z0-9][a-z0-9-]{0,63}`.
- A run ID has the form `run_` followed by 20 lowercase hexadecimal characters.

A run body uses four keys: `input`, `stream`, `debug` and `webhook`. The orchestrator ignores other keys.

## Security

The orchestrator has no authentication. Loopback is the only protection. Any program on the same machine can call every route. This includes the run routes, the async run routes, the catalog routes and the ingestion routes. A program on the same machine can also read and cancel every async run.

The orchestrator refuses to start on a listen address that is not loopback. The upstream services have no authentication either. The orchestrator accepts only loopback `http` or `https` addresses for them.

The ingestion routes can change data. The routes `delete-document` and `rebuild-rag` are on by default. Turn them off with `Ingestion:DisabledRoutes`. See [Turn off an ingestion route](#turn-off-an-ingestion-route).

These checks also apply:

- A request with an `Origin` header gets 403 `request.origin_not_allowed`. Browsers add this header to requests from web pages.
- Text from documents is escaped in the prompt. Text from `prompt.append` steps and templates is trusted, so it is not escaped. Never put text from the caller in trusted text.
- The `system_prompt` input of `rag-qa`, `chat` and `custom-prompt` is not escaped. Send it only from trusted programs.
- The webhook address rule limits where the orchestrator sends requests. It is not authentication. See [Webhooks](#webhooks).

## Errors

Each error has the same body:

```json
{"error": {"type": "not_found_error", "message": "The pipeline does not exist.", "code": "pipeline.not_found", "run_id": null}}
```

| Field | Meaning |
|---|---|
| `type` | The class of the error. The table below shows the type of each code. |
| `message` | A short text for a person. Messages do not repeat input values from the caller. |
| `code` | A stable code. Use this value in your program, not the message. |
| `run_id` | The ID of the run. The value is `null` when no run started. |

When a run started, the `X-Run-Id` header has the same value as `run_id`.

This table lists every code that the orchestrator returns.

| Code | Status | Type | Meaning |
|---|---|---|---|
| `request.content_type` | 400 | `invalid_request_error` | The POST body is not `application/json`. |
| `request.empty` | 400 | `invalid_request_error` | The body is empty, or it is the JSON value `null`. |
| `request.invalid_json` | 400 | `invalid_request_error` | The body is not JSON in the form of the request. |
| `request.invalid` | 400 | `invalid_request_error` | The server rejects a malformed request. |
| `request.too_large` | 413 | `invalid_request_error` | The body is larger than `Api:MaxRequestBodyBytes`, or an upload is larger than `Ingestion:MaxUploadBytes`. |
| `request.mode_invalid` | 400 | `invalid_request_error` | The query parameter `mode` is not `sync` or `async`. |
| `request.origin_not_allowed` | 403 | `invalid_request_error` | The request has an `Origin` header. |
| `pipeline.not_found` | 404 | `not_found_error` | The pipeline name is unknown. |
| `input.invalid` | 400 | `invalid_request_error` | The input does not match the input schema. The message names the path and the rule. |
| `stream.not_supported` | 400 | `invalid_request_error` | The pipeline has `stream: never`, and the request sets `stream` to `true`. |
| `stream.required` | 400 | `invalid_request_error` | The pipeline has `stream: required`, and the request sets `stream` to `false`. |
| `stream.not_supported_async` | 400 | `invalid_request_error` | An async run asks for a stream, or the pipeline has `stream: required`. |
| `webhook.requires_async` | 400 | `invalid_request_error` | The body sets `webhook`, but the request is not async. |
| `webhook.invalid_url` | 400 | `invalid_request_error` | The webhook address is not an absolute `http` or `https` address, or it has a user name or a password. |
| `webhook.host_not_allowed` | 400 | `invalid_request_error` | The webhook host is not loopback and is not in `Webhooks:AllowedHosts`. The orchestrator also refuses a port of its own or of an upstream service on this machine. |
| `admission.full` | 503 | `overloaded_error` | No run slot and no queue place are free. `Retry-After` is 1. |
| `jobs.queue_full` | 503 | `overloaded_error` | The queue of async runs is full. `Retry-After` is 5. |
| `run.not_found` | 404 | `not_found_error` | The run ID is unknown, or the store removed the run. |
| `run.timeout` | 504 | `timeout_error` | The run passed `Runs:RunTimeout`. |
| `run.cancelled` | 499 | `cancelled_error` | The caller disconnected, or the caller cancelled the async run. |
| `run.service_stopped` | 499 | `cancelled_error` | The service stopped while the async run executed. |
| `run.no_answer` | 500 | `internal_error` | The run ended without an answer. The pipeline needs a step that sets the answer. |
| `run.stream_lost` | 500 | `internal_error` | The stream closed before the last event. The caller gets this code in a `run.error` event. |
| `run.internal_error` | 500 | `internal_error` | An unexpected error occurred inside a run. |
| `step.unknown` | 500 | `internal_error` | A step type is not registered. The start checks prevent this. |
| `step.options_invalid` | 400 | `invalid_request_error` | The options of a step fail their rules at run time. For example, an expression gives a wrong value. |
| `expression.invalid` | 400 | `invalid_request_error` | An expression reads a value that cannot be used, such as a list of chunks. |
| `retrieve.profile_unknown` | 400 | `invalid_request_error` | The retrieval alias is not configured. |
| `retrieve.no_query` | 400 | `invalid_request_error` | The retrieval step has no question. |
| `retrieve.query_too_long` | 400 | `invalid_request_error` | The question is longer than 20000 characters. |
| `prompt.user_empty` | 400 | `invalid_request_error` | The prompt of the caller is empty. |
| `prompt.no_user` | 400 | `invalid_request_error` | The pipeline builds the prompt before it has a prompt of the caller. |
| `prompt.chunks_missing` | 400 | `invalid_request_error` | A chunk section names a step that has no chunks. |
| `merge.source_missing` | 400 | `invalid_request_error` | A merge source step has no chunks. |
| `citations.no_answer` | 400 | `invalid_request_error` | The citation step runs before any answer exists. |
| `llm.no_messages` | 400 | `invalid_request_error` | `llm.generate` runs before `prompt.assemble`. |
| `llm.model_unknown` | 400 | `invalid_request_error` | The model alias is not configured. |
| `template.unknown` | 400 | `invalid_request_error` | The prompt template does not exist. |
| `llm.empty_answer` | 502 | `upstream_error` | The model returned an empty answer. |
| `result.uninitialized` | 502 | `upstream_error` | An internal guard for a result that has no value. Report it as a bug. |
| `<upstream>.not_located` | 503 | `service_unavailable_error` | The address of the upstream is unknown. Its ready file is missing or cannot be read. |
| `<upstream>.unreachable` | 503 | `service_unavailable_error` | The upstream is not reachable after the retries. |
| `<upstream>.timeout` | 504 | `timeout_error` | The upstream did not answer within `RequestTimeoutSeconds`. |
| `<upstream>.invalid_response` | 502 | `upstream_error` | The upstream sent an answer that the orchestrator cannot read. |
| `xat-llm-engine.stream_incomplete` | 502 | `upstream_error` | The stream from xat-llm-engine ended before the answer was complete. |
| `<upstream>.<type>` | See the rules below | See the rules below | The upstream returned an error with this type. |
| `<upstream>.http_<status>` | See the rules below | See the rules below | The upstream returned an error without a type. |
| `ingestion.route_disabled` | 404 | `not_found_error` | The route is in `Ingestion:DisabledRoutes`, or `Ingestion:Enabled` is `false`. |
| `ingestion.route_not_found` | 404 | `not_found_error` | The path ends with a slash, or a document ID has a dot segment. xat-rag gets no request. |
| `xat-rag.proxy_failed` | 502 | `upstream_error` | The proxy cannot read the answer of xat-rag. |
| `internal.error` | 500 | `internal_error` | An unexpected error occurred outside a run. |

`<upstream>` is `xat-rag` or `xat-llm-engine`. For an error from an upstream, the status follows the status of the upstream:

- 400, 413 and 415 give 400.
- 404 gives 404. 409 gives 409. 429 gives 429.
- 503 gives 503. A 503 with the type `overloaded_error` gives the type `overloaded_error`.
- 504 gives 504.
- Any other status gives 502.

The code uses the upstream type only when the type is a short plain name. Otherwise the code is `<upstream>.http_<status>`.

Rules for `Retry-After`:

- A 503 always has a `Retry-After` header. The default is 5 seconds.
- A 429 has a `Retry-After` header only when the upstream sent one.
- The value is in whole seconds. The smallest value is 1.

## Routes

| Method and path | Purpose |
|---|---|
| `GET /health/live` | Check that the process runs. |
| `GET /health/ready` | Check the upstream services. |
| `GET /v1/pipelines` | List the pipelines. |
| `GET /v1/pipelines/{name}` | Read one pipeline. |
| `POST /v1/pipelines/{name}/runs` | Run a pipeline. Add `?mode=async` to queue the run. |
| `POST /v1/ask` | Run the pipeline that `Api:DefaultPipeline` names. Add `?mode=async` to queue the run. |
| `GET /v1/steps` | List the step types. |
| `GET /v1/contexts` | List the retrieval aliases. |
| `GET /v1/runs/{id}` | Read an async run. |
| `DELETE /v1/runs/{id}` | Cancel an async run. |
| `GET /v1/rags`, `GET /v1/rags/{rag}`, `PUT /v1/rags/{rag}/documents`, `DELETE /v1/rags/{rag}/documents/{doc_id}`, `POST /v1/rags/{rag}/rebuild`, `GET /v1/jobs/{job_id}`, `DELETE /v1/jobs/{job_id}` | Forward to xat-rag. See [Ingestion proxy](#ingestion-proxy). |

Routes that do not exist give 404 with no JSON body. A wrong method gives 405.

### Health routes

`GET /health/live` returns 200 when the process runs. It does not call the upstream services.

`GET /health/ready` calls each upstream service. Each call has a time limit of 3 seconds.

| Field | Meaning |
|---|---|
| `status` | `ready`, `degraded` or `unavailable`. `degraded` means that an upstream service is down. The orchestrator still runs. `unavailable` gives HTTP status 503. |
| `upstreams[].name` | The name of the upstream service. |
| `upstreams[].status` | `healthy`, `degraded` or `unhealthy`. |
| `upstreams[].description` | A short text for a person. |

Example:

```json
{"status":"degraded","upstreams":[{"name":"xat-rag","status":"degraded","description":"xat-rag is not reachable."},{"name":"xat-llm-engine","status":"healthy","description":"xat-llm-engine is ready."}]}
```

### Catalog routes

`GET /v1/pipelines` returns `{"pipelines": [...]}`, sorted by name. The list changes only after a restart. Each pipeline has these fields: `name`, `version`, `summary`, `stream`, `input_schema` and `steps`. The `steps` field lists the step types in order. The `stream` value is `allowed`, `never` or `required`.

`GET /v1/pipelines/{name}` returns one pipeline in the same form. An unknown name gives 404 `pipeline.not_found`.

```json
{"name":"chat","version":1,"summary":"Sends a prompt to the LLM with no retrieval.","stream":"allowed","input_schema":{"type":"object","required":["prompt"],"additionalProperties":false},"steps":["prompt.system","prompt.user","prompt.assemble","llm.generate"]}
```

`GET /v1/steps` returns `{"steps": [...]}`. Each step type has `name`, `summary` and `traits`. See [Traits](#traits).

`GET /v1/contexts` returns the retrieval aliases from `Retrieval:Profiles`. Each item has `alias`, `context` and `chunks`. This route reads the settings only. It does not check that xat-rag has the context.

### Run a pipeline

`POST /v1/pipelines/{name}/runs` runs a pipeline. `POST /v1/ask` runs the default pipeline in the same way. The query parameter `mode` sets how the run starts. The value `sync` is the default. The value `async` queues the run. See [Asynchronous runs](#asynchronous-runs).

The body has these fields:

| Field | Type | Default | Meaning |
|---|---|---|---|
| `input` | object | `{}` | The input of the pipeline. The input schema checks it. |
| `stream` | boolean | `false` | `true` returns server-sent events. See [Streamed runs](#streamed-runs). |
| `debug` | boolean | `false` | `true` adds the time of each step to the response. |
| `webhook` | string | None | The address of the receiver for an async run. Use it only with `mode=async`. See [Webhooks](#webhooks). |

A sync run returns 200 when the run ends with an answer. The response has these fields:

| Field | Type | Meaning |
|---|---|---|
| `run_id` | string | The ID of the run. |
| `pipeline` | string | The name of the pipeline. |
| `version` | integer | The version of the pipeline file. |
| `status` | string | `answered` when the model wrote the answer. `no_answer` when a step ended the run early because no source matched. |
| `answer.text` | string | The answer. |
| `answer.citations[]` | list | The sources that the answer cites. Each item has `number`, `chunk_id`, `title` and `uri`. The `uri` value is empty unless the scheme is `http` or `https`. |
| `usage` | object | The token counts that xat-llm-engine reported for the run. The fields are `prompt_tokens` and `completion_tokens`. |
| `warnings` | list | Notes about the run. A warning is not an error. |
| `debug` | object or null | The step times, when `debug` is `true`. Each step has `id`, `uses` and `duration_ms`. |

```bash
curl -s -X POST http://127.0.0.1:8430/v1/pipelines/rag-qa/runs \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Which port does the orders service use?"}}'
```

```json
{"run_id":"run_9ace3c5b0fd34d91a5d2","pipeline":"rag-qa","version":1,"status":"answered","answer":{"text":"The orders service uses port 8080 [1].","citations":[{"number":1,"chunk_id":"docs:orders.md#h/ports","title":"Orders","uri":"https://example.test/orders.md"}]},"usage":{"prompt_tokens":100,"completion_tokens":9},"warnings":[],"debug":null}
```

A run that fails returns the error body. The `run_id` field is set when the run started. A failed input check, such as `input.invalid`, is a run failure. A bad body, an unknown pipeline or a refused stream is not. Those errors come before the run starts, so `run_id` is `null` and the `X-Run-Id` header is missing.

### Streamed runs

Set `"stream": true` to receive server-sent events. The pipeline must allow streams. The response has status 200, the content type `text/event-stream` and the `X-Run-Id` header. A failure after the start comes as a `run.error` event, and the HTTP status stays 200.

| Event | Data | When |
|---|---|---|
| `run.started` | `run_id`, `pipeline`, `version` | The first event. |
| `step.started` | `step_id`, `uses` | Before each step. |
| `answer.delta` | `text` | Each new part of the answer. Only steps that stream send it. |
| `answer.citations` | `citations` | After the answer, when the prompt had sources. The list can be empty. |
| `run.completed` | The sync response | The last event of a successful run. |
| `run.error` | The error body | The last event of a failed run. |

```bash
curl -N -X POST http://127.0.0.1:8430/v1/pipelines/rag-qa/runs \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Which port does the orders service use?"}, "stream": true}'
```

```text
event: run.started
data: {"run_id":"run_9374b5dc642a41299e6c","pipeline":"rag-qa","version":1}

event: answer.delta
data: {"text":"The orders service uses port 8080"}

event: run.completed
data: {"run_id":"run_9374b5dc642a41299e6c","pipeline":"rag-qa","version":1,"status":"answered","answer":{...}}
```

The input check of a stream runs after `run.started`. So an input error arrives as a `run.error` event. When the caller closes the connection, the run is cancelled. The cancel also stops a running upstream call.

### Asynchronous runs

An async run starts in the background. Add `?mode=async` to a run route. The route returns 202 at once. The run waits in a queue until a worker takes it. Async runs do not stream.

The run time limit starts when a worker starts the run. Time in the queue does not count.

#### Submit an async run

Send the same body as a sync run. The response is 202:

```http
HTTP/1.1 202 Accepted
Location: /v1/runs/run_857a5e1cfb0143e99eaa
X-Run-Id: run_857a5e1cfb0143e99eaa

{"run_id":"run_857a5e1cfb0143e99eaa","status":"queued","status_url":"/v1/runs/run_857a5e1cfb0143e99eaa"}
```

The `status` value is always `queued` in this response. The `status_url` value is the path to read the run.

The orchestrator checks the input and the webhook address before it accepts the run. A bad input gives 400 `input.invalid`. A bad webhook address gives 400 with a `webhook.` code. In both cases no run is stored. A full queue gives 503 `jobs.queue_full` with `Retry-After: 5`.

#### Read an async run

`GET /v1/runs/{id}` returns the state of the run. These fields are the main fields:

| Field | Meaning |
|---|---|
| `status` | `queued`, `running`, `succeeded`, `failed` or `cancelled`. |
| `created_at`, `started_at`, `completed_at` | Times with an offset. `started_at` and `completed_at` are `null` until the run reaches that point. |
| `result` | The result of a `succeeded` run. It has the same form as a sync response. Otherwise `null`. |
| `error` | The error of a `failed` or `cancelled` run. It has `type`, `message`, `code` and `run_id`. Otherwise `null`. |
| `webhook` | The webhook state, or `null` when the run has no webhook. See [Webhook state](#webhook-state). |

A `succeeded` run with a `no_answer` result has the status `succeeded`. Read `result.status` to see it. A `cancelled` run has the error code `run.cancelled`, or `run.service_stopped` when the service stopped.

```json
{"run_id":"run_3cd46f513abd4f1b9023","pipeline":"rag-qa","version":1,"status":"succeeded","created_at":"2026-11-11T11:23:20.961894+00:00","started_at":"2026-11-11T11:23:20.961917+00:00","completed_at":"2026-11-11T11:23:20.963131+00:00","result":{"run_id":"run_3cd46f513abd4f1b9023","pipeline":"rag-qa","version":1,"status":"answered","answer":{"text":"The orders service uses port 8080 [1].","citations":[{"number":1,"chunk_id":"docs:orders.md#h/ports","title":"Orders","uri":"https://example.test/orders.md"}]},"usage":{"prompt_tokens":100,"completion_tokens":9},"warnings":[],"debug":null},"error":null,"webhook":null}
```

#### Cancel an async run

`DELETE /v1/runs/{id}` cancels the run. The response shows the run as it is after the request.

- A queued run becomes `cancelled` at once. It leaves the queue.
- A running run stops soon after. The response can still show `running`. Read the run again to see the final state.
- A finished run does not change.
- An unknown run gives 404 `run.not_found`.

#### Retention of async runs

The store is in memory. A restart removes all runs. These rules apply to the other runs:

- A finished run stays for `Jobs:RetentionMinutes` after it ends.
- The store keeps at most `Jobs:RetainFinished` finished runs. The oldest finished runs go first.
- Queued and running runs stay until they end.
- A read of a run after its retention time gives 404 `run.not_found`.

### Webhooks

A webhook sends the finished run to a receiver. The orchestrator sends it once, when the run reaches `succeeded`, `failed` or `cancelled`. This includes a run that the caller cancels while it waits.

Set the address in the `webhook` field of the body. The field works only with `mode=async`.

```bash
curl -s -X POST 'http://127.0.0.1:8430/v1/pipelines/rag-qa/runs?mode=async' \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Which port does the orders service use?"}, "webhook": "http://127.0.0.1:9000/hook"}'
```

The orchestrator sends a `POST` request. The request has the headers `Content-Type: application/json; charset=utf-8`, `X-Run-Id` and `X-Webhook-Event: run.finished`. The body has the fields `event`, with the value `run.finished`, and `run`. The `run` object has the same form as the response of `GET /v1/runs/{id}`. In the body, the webhook state is `pending`, and `attempts` is 0.

These rules apply to delivery:

- The orchestrator repeats a try after a connection error, a timeout, status 408, status 429 or a 5xx status.
- Any other status ends the delivery at once. This includes a redirect. The orchestrator never follows a redirect.
- `Webhooks:RetryCount` sets the number of retries. The number of tries is one more than the retries.
- The delay before a retry starts at `Webhooks:RetryDelayMilliseconds`. The delay doubles with each retry, with a random variation. The delay stops growing at 30 seconds.
- `Webhooks:TimeoutSeconds` sets the time limit of one try.
- Deliveries use their own workers. `Jobs:WebhookWorkers` sets their number. A slow receiver does not block other runs.
- A receiver can get the same webhook more than once. Delivery is at least once.

#### Webhook state

The `webhook` field of a run shows the delivery state:

| Field | Meaning |
|---|---|
| `url` | The scheme, the host and the port. The path and the query are never shown. |
| `status` | `pending` before the delivery ends. `delivered` when the receiver accepted the webhook. `failed` when the delivery did not succeed. |
| `attempts` | The number of tries. |
| `error` | The reason for a failed delivery. The text includes the status of the receiver when the receiver answered. Otherwise `null`. |

A failed delivery does not change the run result. The orchestrator does not send a failed webhook again.

Your receiver must follow these rules:

- Answer with a 2xx status at once. Process the body after the answer.
- Ignore a repeat. Use the `X-Run-Id` header to find a repeat.
- Do not rely on the webhook as proof. Webhooks have no signature.

#### Webhook address rule

The orchestrator checks the address when the run is submitted. A bad address gives 400, and no run is stored.

- The address must be absolute. It must use `http` or `https`, and it must not have a user name or a password. Otherwise the code is `webhook.invalid_url`.
- The host must be a loopback host (`127.0.0.1`, `localhost` or `::1`), or it must be in `Webhooks:AllowedHosts`. Otherwise the code is `webhook.host_not_allowed`.
- Host names in `Webhooks:AllowedHosts` ignore case. Any port is allowed.
- The value `*` in `Webhooks:AllowedHosts` allows every host. Use it only in a test.
- Even with `*`, the orchestrator refuses a target on this machine that uses its own port or the port of an upstream `BaseUrl`.
- The rule checks the host name only. A host name can resolve to any address.

The error messages never repeat the address. The orchestrator stores only the scheme, the host and the port. It keeps the full address in memory until the delivery ends.

### Ingestion proxy

The ingestion proxy forwards seven routes to xat-rag. A caller then uses one address for runs and for ingestion. The proxy does not check the content of an upload. xat-rag checks it.

| Route name | Method and path | Purpose |
|---|---|---|
| `list-rags` | `GET /v1/rags` | List the RAGs. |
| `get-rag` | `GET /v1/rags/{rag}` | Read one RAG. |
| `upload-documents` | `PUT /v1/rags/{rag}/documents` | Upload documents. xat-rag starts an ingestion job. |
| `delete-document` | `DELETE /v1/rags/{rag}/documents/{doc_id}` | Delete one document. |
| `rebuild-rag` | `POST /v1/rags/{rag}/rebuild` | Rebuild one RAG. |
| `get-ingestion-job` | `GET /v1/jobs/{job_id}` | Read an ingestion job of xat-rag. |
| `cancel-ingestion-job` | `DELETE /v1/jobs/{job_id}` | Ask an ingestion job of xat-rag to stop. |

The route name is the value for `Ingestion:DisabledRoutes`.

The proxy forwards no other path. Any other path gives 404 with no body, and xat-rag gets no request. A wrong method gives 405.

These rules apply:

- The RAG name must match `[a-z0-9][a-z0-9-]{0,63}`. The match is case-sensitive.
- The job ID must match `[A-Za-z0-9_-]{1,64}`.
- A path that ends with a slash gives 404 `ingestion.route_not_found`.
- The proxy sends the query string and the body to xat-rag unchanged.
- A document ID can hold a slash. Send the slash as `%2F`. An ID with a dot segment, such as `.` or `..`, gives 404 `ingestion.route_not_found`.
- The proxy sends only a short list of request headers to xat-rag. Credentials and cookies are not sent. The `Host` header names xat-rag.
- The proxy does not retry a request. It reads the address of xat-rag again after a failed connection.
- The answer of xat-rag, including its error answers, goes back to the caller as it is.
- The job routes read ingestion jobs of xat-rag. They do not read async runs. Use `GET /v1/runs/{id}` for an async run.

Uploads use `Ingestion:MaxUploadBytes`. An upload that declares a size above the limit gets 413 before xat-rag receives any byte. An upload without a declared size stops at the limit. The other routes use `Api:MaxRequestBodyBytes`. The proxy uses the codes in the [error table](#errors).

#### Turn off an ingestion route

Turning off a route gives 404 to every caller of the orchestrator. A program on the same machine can still call xat-rag directly. Turning off a route does not protect the data in xat-rag.

1. Open `appsettings.json`. For a release, use the file next to the program. For a source build, use `src/Xat.Orchestrator.Api/appsettings.json`.
2. Add the route names to `DisabledRoutes` in the `Ingestion` section. For example:

    ```json
    "DisabledRoutes": [ "delete-document", "rebuild-rag" ]
    ```

3. Restart the orchestrator. An unknown route name stops the start.
4. Send a request to the route. It gives 404 `ingestion.route_disabled`.

You can also pass a name on the command line, for example `--Ingestion:DisabledRoutes:0=delete-document`. To turn off the whole proxy, set `Ingestion:Enabled` to `false`.

## Pipeline files

A pipeline is a YAML file in the folder that `Pipelines:Folder` names. The file extension is `.yaml` or `.yml`. The orchestrator reads all files at start. A file that is not valid stops the start. The message names the file and the pipeline.

A relative folder starts at the folder of the program, not at the current folder of the shell.

### Top-level keys

| Key | Required | Default | Meaning |
|---|---|---|---|
| `name` | Yes | None | The unique name of the pipeline. It matches `[a-z0-9][a-z0-9-]{0,63}`. |
| `version` | No | `1` | An integer of 1 or higher. Change it when the pipeline changes. |
| `summary` | No | Empty | A short text for callers. |
| `stream` | No | `allowed` | `allowed` lets the caller choose. `never` refuses a stream. `required` allows only a stream, so async runs are refused. The value is not case-sensitive. |
| `input` | No | Any input | A JSON Schema, written in YAML. See [Input schema](#input-schema). |
| `steps` | Yes | None | A list of steps. At least one step must set the answer. |

A key that is not in this table stops the start. The message lists the known keys.

### Steps in a file

| Key | Required | Default | Meaning |
|---|---|---|---|
| `uses` | Yes | None | The step type, for example `rag.retrieve`. |
| `id` | No | The `uses` value with dots replaced by hyphens | The unique ID of the step. It matches `[a-z0-9][a-z0-9_-]{0,63}`. A repeated default gets a number, for example `rag-retrieve-2`. |
| `with` | No | Empty mapping | The options of the step. |

A key that is not in this table stops the start.

Plain values have a type. `7` is a number, and `true` is a flag. Quoted values are always text. Put a number in quotes when you need text.

Use [expressions](#expressions) in the `with` block to read the input and the results of earlier steps.

### Start checks

The start checks each pipeline. A failed check stops the start, and the message names the pipeline and the step.

- The name, the version and the step IDs have the right form. Step IDs are unique.
- Each step type exists.
- The options match the step type. An unknown option is an error.
- The `profile`, `model` and `template` options name a configured alias or an existing template. A value that contains `${` is checked when the run starts.
- An expression path starts with `input.` or `steps.`. A `steps.` path names an earlier step.
- At least one step sets the answer.
- A pipeline with `stream: required` has a step that streams.
- The input schema uses only supported keywords.

Example message:

```text
Pipeline 'bad': step 'not-a-step' uses the unknown step type 'not.a.step'.
```

## Built-in steps

The steps below are the built-in step types. An option is optional unless the table says that it is required. A step stores results that later steps read as `steps.<id>.<name>`. A step without results stores nothing that an expression can read.

### Traits

Each step type has traits. The `GET /v1/steps` route lists them.

| Trait | Meaning |
|---|---|
| `CallsLlm` | The step calls xat-llm-engine. It does so only through the model port, which applies the `Parallel` limit of the model. A step type cannot have both `CallsLlm` and `Idempotent`. |
| `Streams` | The step sends `answer.delta` events. A pipeline with `stream: required` needs a step with this trait. |
| `ProducesAnswer` | The step can end the run with an answer. A pipeline needs at least one such step. |
| `Idempotent` | The step can run again after an upstream failure, when it has not sent an event yet. See the rules below. |

An idempotent step runs again, up to `Runs:IdempotentRetryCount` times. The retry happens only after an upstream failure of these kinds: a 5xx status other than 503 and 504, an answer that the orchestrator cannot read, or a stream that was cut off. The HTTP retries of each upstream call run first. Timeouts and unreachable upstreams do not trigger a step retry.

### Typed options

A number or flag option can hold one expression on its own, for example `chunks: "${input.n}"`. The value is checked when the run starts. A value of the wrong type gives 400 `step.options_invalid`. Mixed text in such an option, for example `"5${input.n}"`, stops the start.

### prompt.system

Sets the system prompt of the run. An empty text sets no system prompt.

| Option | Meaning |
|---|---|
| `text` | The system prompt. |
| `template` | The reference `name@version` of a prompt template. |

Set exactly one of `text` and `template`.

### prompt.user

Sets the prompt of the caller. The option `text` is required. Usually it is `${input.prompt}`. An empty text gives 400 `prompt.user_empty`.

### prompt.context

Adds context that the caller sent. The assemble step escapes the text and puts it in a `<context>` tag. An empty text adds nothing.

| Option | Default | Meaning |
|---|---|---|
| `text` | None | The context text. Usually `${input.context}`. |
| `label` | `caller-context` | The name of the section. Use it in the `order` option of `prompt.assemble`. |

### prompt.append

Adds an instruction that the pipeline author wrote. The text is trusted. The assemble step does not escape it, so never put text from the caller in it.

| Option | Default | Meaning |
|---|---|---|
| `text` | None | The instruction text. |
| `template` | None | The reference `name@version` of a prompt template. |
| `name` | `extra` | The name of the section. Use it in the `order` option of `prompt.assemble`. |

Set exactly one of `text` and `template`.

Templates are Liquid files in `Prompts:Folder`. The file name is `name@version.liquid`. A template gets one value, named `input`, which holds the input of the run.

### rag.retrieve

Asks one xat-rag context for the chunks that match a question. A pipeline can use this step more than once.

| Option | Default | Meaning |
|---|---|---|
| `profile` | None, required | The retrieval alias. See `GET /v1/contexts`. |
| `chunks` | The value of the alias | The highest number of chunks. From 1 to 100. |
| `query` | The prompt of the caller | The question. |

| Result | Meaning |
|---|---|
| `steps.<id>.count` | The number of chunks. |
| `steps.<id>.omitted` | The number of candidates that xat-rag did not return. |
| `steps.<id>.chunks` | The chunks. An expression cannot read this list. Use `flow.complete-if` or `retrieval.merge` instead. |

### retrieval.merge

Merges the chunks of earlier retrieval steps into one list. The step takes chunks by rank, one from each source in turn. It does not compare scores. It replaces the chunk sections of the sources in the prompt with one section.

| Option | Default | Meaning |
|---|---|---|
| `from` | None, required | The list of earlier retrieval steps to merge. The list has at least one step. |
| `dedupe` | `true` | Removes chunks with the same ID. |
| `maxChunks` | No limit | The highest number of chunks in the result. From 1 to 1000. |

Results: `steps.<id>.count` and `steps.<id>.chunks`.

### flow.complete-if

Ends the run early with a fixed answer when a condition is true. The run status is `no_answer`. The steps after this step do not run. Use this step for the path without sources.

| Option | Meaning |
|---|---|
| `key` | The path of the value to test. It starts with `input.` or `steps.<id>.<name>`. Required. |
| `isEmpty` | `true` ends the run when the value is empty. `false` ends it when the value is not empty. |
| `equals` | The run ends when the value, as text, equals this text. |
| `text` | The fixed answer. Required. |

Set exactly one of `isEmpty` and `equals`.

### prompt.assemble

Builds the chat messages from all sections that the earlier steps added. The step does these tasks:

- It escapes the text of chunks and of `prompt.context` steps, and it puts them in tags.
- It numbers the sources, so the model can cite them as `[n]`.
- It does not escape the prompt of the caller. The prompt of the caller is the user message.

The step sends every chunk. It does not limit the size of the prompt.

| Option | Default | Meaning |
|---|---|---|
| `order` | The order in which the steps added the sections | The order of the sections. A section that the list does not name goes after the named sections. |

Errors: `prompt.no_user` and `prompt.chunks_missing`.

### llm.generate

Sends the messages to xat-llm-engine and stores the answer. When the caller streams, the step sends each new part of the answer as an `answer.delta` event.

| Option | Default | Meaning |
|---|---|---|
| `model` | `Llm:DefaultModel` | The model alias. |
| `temperature` | The default of the engine | A number from 0 to 2. The step sends it only when the model lists `temperature` in `AllowedFields`. |
| `maxTokens` | The default of the engine | An integer from 1 to 1000000. The step sends it only when the model lists `max_tokens` or `max_completion_tokens` in `AllowedFields`. |

The step adds the token usage to the run. It adds a warning when the model stops at the token limit.

Errors: `llm.no_messages`, `llm.model_unknown` and `llm.empty_answer`.

### answer.verify-citations

Checks the citations in the answer. The model cites a source with its number, for example `[2]`. The step maps each number to its chunk. It adds a warning when a number has no source, and when the answer has no citation. When the prompt had sources, it sends an `answer.citations` event.

This step has no options. A link keeps its value when its scheme is `http` or `https`, or when it has no colon. Other links, such as `javascript:` links, become empty.

## Expressions

An expression is a text inside `${` and `}`. Use expressions in the `with` block of a step.

| Form | Meaning |
|---|---|
| `${input.prompt}` | The value of the input key `prompt`. |
| `${steps.retrieve.count}` | The value of a result of an earlier step. |
| `${input.context ?? 'default'}` | The value, or the default text when the value is missing. The quotes are optional. |

Expressions are lookups. They have no operators, functions or scripts.

These rules apply:

- A text that is only one expression keeps the type of the value. A number stays a number, and an object stays an object.
- A missing value becomes `null`. Inside longer text, it becomes an empty text, unless there is a default.
- Inside longer text, an expression becomes text. An object or a list becomes JSON text.
- A list of chunks cannot be used in an expression. The run fails with `expression.invalid`.

## Input schema

The `input` key of a pipeline is a JSON Schema, written in YAML. The orchestrator checks the input of each run against it. The orchestrator supports only the keywords below. A pipeline that uses another keyword does not start.

- `type`, with `string`, `number`, `integer`, `boolean`, `object`, `array` or `null`. A list of types is allowed.
- `properties` and `required`.
- `additionalProperties`, with the value `false` only.
- `items`, `enum`, `minLength`, `maxLength`, `minimum`, `maximum`, `minItems` and `maxItems`.

The keywords `title`, `description`, `default`, `examples`, `$schema` and `$id` are allowed. They have no effect on the check. An `enum` compares values, not the text of the values.

A message names the path of the value and the rule that failed:

```text
input.language: the value is not one of the allowed values.
```

A pipeline without an `input` key accepts any input.

## Shipped pipelines

The repository has four pipelines in `pipelines/`. Read the YAML file for the full flow.

| Pipeline | Input | What it does |
|---|---|---|
| `rag-qa` (default) | `prompt` (required), `context` (a retrieval alias, up to 64 characters), `system_prompt` | Answers from one retrieval alias and cites the sources. Ends with status `no_answer` when no chunk matches. |
| `chat` | `prompt` (required), `system_prompt` | Sends the prompt to the model with no retrieval. |
| `rag-sequential` | `prompt` (required) | Retrieves from the aliases `docs`, `jira` and `diffs`, merges the chunks and answers. The caller cannot choose the aliases. |
| `custom-prompt` | `prompt` (required), `system_prompt`, `context`, `language` | Adds the caller context and a rule template, then answers. `language` accepts only `English`, `German`, `French`, `Spanish` or `Greek`. |

Do not put text from the caller in a trusted part, such as a template. Use an enum in the input schema instead, as `custom-prompt` does for `language`.

## Settings

The settings come from `appsettings.json`. For a release, the file is next to the program. For a source build, the file is `src/Xat.Orchestrator.Api/appsettings.json`.

You can also set a value on the command line or as an environment variable. The command line form is `--Api:MaxConcurrentRuns=4`. The environment form replaces `:` with `__`, for example `Api__MaxConcurrentRuns`. A list uses an index, for example `--Webhooks:AllowedHosts:0=hooks.example.com`.

Every value is checked at start. A wrong value stops the start with exit code 1 and a message that names the key.

The `Urls` setting sets the listen address. The `--urls` option and the `ASPNETCORE_URLS` variable also set it. The address must be loopback.

### Api and runs

| Setting | Default | Meaning |
|---|---|---|
| `Api:DefaultPipeline` | `rag-qa` | The pipeline that `POST /v1/ask` runs. The pipeline must exist. |
| `Api:MaxConcurrentRuns` | `8` | The number of runs that the run routes handle at the same time. The value must be 1 or higher. |
| `Api:QueueLimit` | `16` | The number of runs that wait for a free run slot. The value must be 0 or higher. |
| `Api:MaxRequestBodyBytes` | `4194304` (4 MiB) | The largest request body in bytes. The value must be 1024 or higher. |
| `Runs:RunTimeout` | `00:05:00` | The time limit of one run. The value must be more than zero and at most 1 hour. |
| `Runs:IdempotentRetryCount` | `1` | The number of extra tries of an idempotent step after an upstream failure. `0` turns this off. From 0 to 3. |

### Jobs

Jobs settings control async runs. The store is in memory, so these settings do not keep runs after a restart.

| Setting | Default | Meaning |
|---|---|---|
| `Jobs:Workers` | `2` | The number of async runs that execute at the same time. From 1 to 32. |
| `Jobs:WebhookWorkers` | `4` | The number of webhooks that the orchestrator sends at the same time. From 1 to 32. |
| `Jobs:QueueLimit` | `64` | The number of async runs that wait for a worker. A run above this number gets 503 `jobs.queue_full`. From 1 to 10000. |
| `Jobs:RetainFinished` | `500` | The highest number of finished runs that the store keeps. From 1 to 100000. |
| `Jobs:RetentionMinutes` | `60` | The number of minutes that the store keeps a finished run. From 1 to 10080. |

### Webhooks

| Setting | Default | Meaning |
|---|---|---|
| `Webhooks:AllowedHosts` | Empty list | The host names that can receive webhooks, besides loopback hosts. The value `*` allows every host. Host names ignore case. The list must not have an empty entry. |
| `Webhooks:TimeoutSeconds` | `10` | The time limit of one try. From 1 to 120. |
| `Webhooks:RetryCount` | `3` | The number of retries after a failed try. From 0 to 10. |
| `Webhooks:RetryDelayMilliseconds` | `1000` | The delay before the first retry. The delay doubles with each retry. From 0 to 60000. |

To allow another host, add its name to `Webhooks:AllowedHosts` and restart. On the command line, use `--Webhooks:AllowedHosts:0=hooks.example.com`.

### Ingestion

| Setting | Default | Meaning |
|---|---|---|
| `Ingestion:Enabled` | `true` | Turns the ingestion proxy on or off. |
| `Ingestion:DisabledRoutes` | Empty list | The route names to turn off. See [Ingestion proxy](#ingestion-proxy). An unknown name stops the start. |
| `Ingestion:MaxUploadBytes` | `67108864` (64 MiB) | The largest upload in bytes. From 1024 to 1073741824. The limit applies to `upload-documents` only. |
| `Ingestion:ActivityTimeoutSeconds` | `120` | The time without data before the proxy ends a request. From 1 to 3600. |

### Upstream services

| Setting | Default | Meaning |
|---|---|---|
| `Upstreams:XatRag:BaseUrl` | `http://127.0.0.1:8421` | The address of xat-rag. |
| `Upstreams:XatRag:ReadyFile` | Empty | The path of a JSON file that holds the address in the key `url`. |
| `Upstreams:XatRag:RequestTimeoutSeconds` | `30` | The time limit of one call, with all retries. From 1 to 3600. |
| `Upstreams:XatRag:RetryCount` | `2` | The number of retries. From 0 to 5. |
| `Upstreams:XatLlmEngine:BaseUrl` | `http://127.0.0.1:8420` | The address of xat-llm-engine. |
| `Upstreams:XatLlmEngine:ReadyFile` | Empty | The same as for xat-rag. |
| `Upstreams:XatLlmEngine:RequestTimeoutSeconds` | `180` | The time limit of one call, with all retries. From 1 to 3600. |
| `Upstreams:XatLlmEngine:RetryCount` | `2` | The number of retries. From 0 to 5. |

An address must be an `http` or `https` loopback address with no path, query or fragment. For example, use `http://127.0.0.1:8421`.

Set `BaseUrl`, or set `ReadyFile`. When both are empty, the start stops. When `BaseUrl` is set, the orchestrator does not read `ReadyFile`. Use a ready file when the port changes at each start. The orchestrator reads the file again after a failed connection.

Each call retries only after a 429, 503 or 504 answer, or after a failed connection. The wait uses the `Retry-After` value when the upstream sends one. Otherwise the wait starts at 500 milliseconds and grows.

The start logs a warning when `RequestTimeoutSeconds` is not shorter than `Runs:RunTimeout`. The run timeout then always comes first.

### Retrieval and models

| Setting | Default | Meaning |
|---|---|---|
| `Retrieval:Profiles:<alias>:Context` | None, required | The name of the xat-rag context. |
| `Retrieval:Profiles:<alias>:Chunks` | `8` | The default number of chunks. From 1 to 100. |
| `Llm:DefaultModel` | `default` | The model alias that steps use when they name no model. The alias must exist. |
| `Llm:Models:<alias>:Model` | None, required | The model ID, in the form `provider/model`. |
| `Llm:Models:<alias>:Parallel` | `1` | The number of calls to this model at the same time. The value must be 1 or higher. Aliases that share a model ID must have the same value. A call that finds no free place waits. The wait counts against `Runs:RunTimeout`. |
| `Llm:Models:<alias>:AllowedFields` | Empty list | The request fields that the orchestrator may send. The list can hold `temperature`, `max_tokens`, `max_completion_tokens` and `stream_options`. |

The shipped alias `default` uses the model `local/ministral-3-3b`. A key under `Llm` that the orchestrator does not know stops the start. The message names the key.

### Pipelines, prompts and hosts

| Setting | Default | Meaning |
|---|---|---|
| `Pipelines:Folder` | `pipelines` | The folder with the pipeline files. |
| `Prompts:Folder` | `prompts` | The folder with the template files. |
| `Logging:LogLevel:Default` | `Information` | The default log level. |
| `AllowedHosts` | `localhost`, `127.0.0.1` and `[::1]` | The host names that the orchestrator accepts in the `Host` header. This setting is not the webhook list. |

## Limits

| Limit | Value | Setting |
|---|---|---|
| Request body | 4 MiB | `Api:MaxRequestBodyBytes` |
| Upload through the ingestion proxy | 64 MiB | `Ingestion:MaxUploadBytes` |
| Runs at the same time | 8 | `Api:MaxConcurrentRuns` |
| Runs that wait for a slot | 16. A run above this gets 503 `admission.full`. | `Api:QueueLimit` |
| Time of one run | 5 minutes. At most 1 hour. A run above the limit gets 504 `run.timeout`. | `Runs:RunTimeout` |
| Time of one upstream call | 30 seconds for xat-rag. 180 seconds for xat-llm-engine. The time covers all retries. | `Upstreams:*:RequestTimeoutSeconds` |
| Retries of one upstream call | 2. At most 5. The ingestion proxy does not retry. | `Upstreams:*:RetryCount` |
| Question for retrieval | 20000 characters | Fixed |
| Chunks in one retrieval | 8 by default. At most 100. | `Retrieval:Profiles:<alias>:Chunks` and the `chunks` option |
| Async runs at the same time | 2 | `Jobs:Workers` |
| Async runs that wait | 64. A run above this gets 503 `jobs.queue_full`. | `Jobs:QueueLimit` |
| Finished runs kept | 500, for 60 minutes | `Jobs:RetainFinished` and `Jobs:RetentionMinutes` |
| Webhook tries | 4 tries in total, each with a 10 second limit | `Webhooks:RetryCount` and `Webhooks:TimeoutSeconds` |
| Inactivity of an ingestion request | 120 seconds. A request without data for this time gets 504 `xat-rag.timeout`. | `Ingestion:ActivityTimeoutSeconds` |

The orchestrator does not limit the size of a prompt. It sends every chunk that a retrieval returns. xat-llm-engine decides which prompt sizes it accepts.
