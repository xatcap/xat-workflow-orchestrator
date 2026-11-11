# API reference

This page lists the HTTP routes, the pipeline file format and the settings of xat-workflow-orchestrator. It also describes the OpenAI-compatible route and the ingestion proxy. To start the orchestrator, read the [quickstart](quickstart.md). For the design, read the [architecture](architecture.md).

- [Conventions](#conventions)
- [Security](#security)
- [Errors](#errors)
- [Routes](#routes)
- [Streamed runs](#streamed-runs)
- [Asynchronous runs](#asynchronous-runs)
- [Webhooks](#webhooks)
- [OpenAI-compatible API](#openai-compatible-api)
- [Ingestion proxy](#ingestion-proxy)
- [Pipeline files](#pipeline-files)
- [Built-in steps](#built-in-steps)
- [Expressions](#expressions)
- [Input schema](#input-schema)
- [Shipped pipelines](#shipped-pipelines)
- [Settings](#settings)
- [Limits](#limits)

## Conventions

The default base URL is `http://127.0.0.1:8430`. The `Urls` setting changes it. Use a loopback address only. See [Security](#security).

Bodies are JSON in UTF-8. Response names use snake_case, for example `run_id`. A POST body must have the content type `application/json`. Another content type gives 400 `request.content_type`. The OpenAI-compatible route and the ingestion proxy have their own rules. See their sections.

Pipeline names, retrieval aliases and model aliases match `[a-z0-9][a-z0-9-]{0,63}`. Step IDs in a pipeline file also allow an underscore.

A run ID has the form `run_` followed by 20 lowercase hexadecimal characters.

A run body reads these top-level keys:

- `input`
- `stream`
- `debug`
- `webhook`

The orchestrator ignores any other top-level key. The input schema of the pipeline checks the keys inside `input`. The `webhook` key works only with `mode=async`.

The query parameter `mode` sets how a run starts. The value `sync` is the default. The value `async` queues the run and returns 202. Another value gives 400 `request.mode_invalid`. See [Asynchronous runs](#asynchronous-runs).

The orchestrator accepts only the host names `localhost`, `127.0.0.1` and `[::1]`, with or without a port. Another host name gets 400 with an HTML page. That page does not use the error form.

## Security

The orchestrator has no authentication and no API key. This is a decision of the project owner. Access control will come later. The OpenAI-compatible route accepts any API key and ignores it.

The orchestrator listens on loopback only. It refuses to start when a listen address is not loopback. The check covers the `urls` setting, the `--urls` option, the `ASPNETCORE_URLS` variable and the `Kestrel:Endpoints` setting. If the server binds to another address, the orchestrator stops and exits with code 1.

The upstream services also have no authentication. The orchestrator accepts only loopback `http` addresses for xat-rag and xat-llm-engine.

Any program on the same machine can call every route. This includes the run routes, the asynchronous run routes, the catalog routes, the OpenAI-compatible route and the ingestion routes. A program on the same machine can read and cancel every asynchronous run.

Warning: the ingestion routes have no authentication. Any program on the same machine can upload documents, delete documents and rebuild a RAG. The routes that delete or rebuild are on by default. Turn them off with `Ingestion:DisabledRoutes`. See [Ingestion proxy](#ingestion-proxy).

The orchestrator also has these protections:

- A POST body must have the content type `application/json`. A web form cannot send that type, so a cross-site form post is refused.
- A request with an `Origin` header gets 403 `request.origin_not_allowed`. A browser adds this header to a request from a web page. So a web page cannot call the orchestrator, even with a request that needs no preflight. The error has the normal form on every route, including the OpenAI-compatible route. Programs such as `curl` send no `Origin` header, so they are not affected. A browser-based client cannot use the orchestrator.
- The Host filter refuses other host names. See [Conventions](#conventions).
- The orchestrator sends no CORS headers.
- Error responses never show exception text.
- Text from documents is untrusted. The prompt step escapes it. See [architecture](architecture.md#security).
- Admission control limits the work that reaches the upstream services. It is not authorization.
- The webhook address rule limits where the orchestrator sends requests. It is not authentication. See [Webhooks](#webhooks).

## Errors

Each error has the same body. The OpenAI-compatible route is the exception. It has its own body, except for the 403 of a request with an `Origin` header, which has the standard body. See [OpenAI-compatible errors](#openai-compatible-errors).

```json
{"error": {"type": "not_found_error", "message": "The pipeline does not exist.", "code": "pipeline.not_found", "run_id": null}}
```

| Field | Meaning |
|---|---|
| `type` | The class of the error. See the table below. |
| `message` | A short text for a person. It does not repeat input values from the caller. A message from an upstream service can appear, up to 300 characters. |
| `code` | A stable code for a program. Use this value in your logic. |
| `run_id` | The ID of the run, or `null` when no run started. |

The class of an error sets the HTTP status and the `type`:

| Type | Status | Used for |
|---|---|---|
| `invalid_request_error` | 400 | A bad body, bad input, a bad step option, a prompt that is too large, a bad `mode` or a bad webhook address. |
| `invalid_request_error` | 403 | The request has an `Origin` header. See [Security](#security). |
| `invalid_request_error` | 413 | The body is larger than `Api:MaxRequestBodyBytes`, or an upload is larger than `Ingestion:MaxUploadBytes`. |
| `not_found_error` | 404 | An unknown pipeline, model alias or run. A route that the operator turned off. |
| `conflict_error` | 409 | An upstream reports a conflict. |
| `overloaded_error` | 503 | The orchestrator has no free run slot, the queue of asynchronous runs is full, or an upstream is overloaded. |
| `rate_limit_error` | 429 | An upstream limits the rate of requests. |
| `timeout_error` | 504 | A run passed its time limit, or an upstream did not answer in time. |
| `service_unavailable_error` | 503 | An upstream is not reachable or not ready. |
| `upstream_error` | 502 | An upstream returned a failure, or sent an answer that the orchestrator cannot read. |
| `cancelled_error` | 499 | The run was cancelled. For example, the caller disconnected, or the caller deleted an asynchronous run. |
| `internal_error` | 500 | A failure inside the orchestrator. The body has no details. |

The main codes are these:

| Code | Status | Cause |
|---|---|---|
| `request.content_type` | 400 | A POST body does not have the content type `application/json`. |
| `request.invalid_json` | 400 | The body is not JSON of the expected form. |
| `request.empty` | 400 | The body is empty, or it is the JSON value `null`. |
| `request.too_large` | 413 | The body is larger than `Api:MaxRequestBodyBytes`. An upload is larger than `Ingestion:MaxUploadBytes`. |
| `request.mode_invalid` | 400 | The query parameter `mode` is not `sync` or `async`. |
| `request.origin_not_allowed` | 403 | The request has an `Origin` header, so it may come from a web page. See [Security](#security). |
| `pipeline.not_found` | 404 | The pipeline name is unknown. |
| `input.invalid` | 400 | The input does not match the input schema. The message names the path and the rule. An asynchronous run gets this error at submit. |
| `stream.not_supported` | 400 | The pipeline has `stream: never`, and the request sets `stream` to `true`. |
| `stream.required` | 400 | The pipeline has `stream: required`, and the request sets `stream` to `false`. |
| `stream.not_supported_async` | 400 | The request sets `stream` to `true` with `mode=async`, or the pipeline has `stream: required` and the run is asynchronous. |
| `webhook.requires_async` | 400 | The body sets `webhook`, and the request does not have `mode=async`. |
| `webhook.invalid_url` | 400 | The webhook address is not an absolute `http` or `https` address, or it has a user name or a password. |
| `webhook.host_not_allowed` | 400 | The host of the webhook address is not a loopback host, and it is not in `Webhooks:AllowedHosts`. |
| `retrieve.profile_unknown` | 400 | The retrieval alias in a `rag.retrieve` step is not configured. |
| `retrieve.no_query` | 400 | The step has no question text. |
| `retrieve.query_too_long` | 400 | The question is longer than 20000 characters. |
| `prompt.user_empty` | 400 | The prompt of the caller is empty. |
| `prompt.too_large` | 400 | The fixed parts of the prompt do not fit the context window of the model. |
| `expression.invalid` | 400 | An expression reads a value that cannot be used in text or JSON. |
| `llm.model_unknown` | 404 | The model alias in a step is not configured. |
| `llm.empty_answer` | 502 | The model returned an empty answer. |
| `run.timeout` | 504 | The run passed `Runs:RunTimeout`. |
| `run.cancelled` | 499 | The run was cancelled. An asynchronous run that the caller deletes gets this code too. |
| `admission.full` | 503 | All run slots and all queue places are in use. |
| `jobs.queue_full` | 503 | The queue of asynchronous runs is full. The `Retry-After` value is 5 seconds. |
| `run.not_found` | 404 | The run ID is unknown, or the orchestrator removed the run. See [Retention](#retention). |
| `xat-rag.unreachable` | 503 | xat-rag is not reachable after the retries. The ingestion routes do not retry. |
| `xat-rag.timeout` | 504 | An ingestion route got no data from xat-rag for `Ingestion:ActivityTimeoutSeconds`. |
| `xat-rag.proxy_failed` | 502 | The ingestion proxy cannot read the answer of xat-rag, or it failed for another reason. See [Ingestion proxy](#ingestion-proxy). |
| `ingestion.route_disabled` | 404 | The operator turned off this ingestion route, or the proxy is off. See [Ingestion proxy](#ingestion-proxy). |
| `ingestion.route_not_found` | 404 | The path ends with a slash, or a document ID has a dot segment. xat-rag gets no request. See [Ingestion proxy](#ingestion-proxy). |
| `xat-llm-engine.unreachable` | 503 | xat-llm-engine is not reachable after the retries. |
| `<upstream>.<type>` | Depends on the upstream answer | An error from an upstream service. The type comes from the upstream body. |
| `internal.error` | 500 | An unexpected error outside a run. |
| `run.internal_error` | 500 | An unexpected error inside a run. |

An upstream answer maps to one of these caller errors:

| Upstream answer | Caller gets |
|---|---|
| 400, 413 or 415 | 400 `invalid_request_error` |
| 404 | 404 `not_found_error` |
| 409 | 409 `conflict_error` |
| 429 | 429 `rate_limit_error` |
| 503 with the type `overloaded_error` | 503 `overloaded_error` |
| 503, other types | 503 `service_unavailable_error` |
| 504 | 504 `timeout_error` |
| Any other status | 502 `upstream_error` |

When an upstream body is not in the error form, the code is `<upstream>.http_<status>`. A failed connection gives `<upstream>.unreachable`. A timeout gives `<upstream>.timeout`.

The `Retry-After` header follows these rules:

- A 503 always has a `Retry-After` header. The value comes from the error. When the error has no value, the value is 5 seconds.
- A refused run gets 1 second from the admission control.
- A 429 has a `Retry-After` header when the upstream sent one.
- Other statuses have no `Retry-After` header.
- The value is in whole seconds. The smallest value is 1.

Example of an error from the admission control (real output):

```http
HTTP/1.1 503 Service Unavailable
Retry-After: 1

{"error":{"type":"overloaded_error","message":"The orchestrator has no free capacity. Try again later.","code":"admission.full","run_id":null}}
```

## Routes

| Method and path | Purpose |
|---|---|
| [`GET /health/live`](#get-healthlive) | Check that the process runs. |
| [`GET /health/ready`](#get-healthready) | Check the upstream services. |
| [`GET /v1/pipelines`](#get-v1pipelines) | List the pipelines. |
| [`GET /v1/pipelines/{name}`](#get-v1pipelinesname) | Show one pipeline. |
| [`POST /v1/pipelines/{name}/runs`](#post-v1pipelinesnameruns) | Run a pipeline. Add `?mode=async` to queue the run. |
| [`POST /v1/ask`](#post-v1ask) | Run the default pipeline. Add `?mode=async` to queue the run. |
| [`GET /v1/steps`](#get-v1steps) | List the step types. |
| [`GET /v1/contexts`](#get-v1contexts) | List the retrieval aliases. |
| [`GET /v1/models`](#get-v1models) | List the pipelines that the OpenAI-compatible route can call, and the model aliases. |
| [`POST /v1/chat/completions`](#openai-compatible-api) | Run a pipeline with the OpenAI chat format. The model is `pipeline/<name>`. |
| [`GET /v1/runs/{id}`](#read-a-run) | Read the state of an asynchronous run. |
| [`DELETE /v1/runs/{id}`](#cancel-a-run) | Cancel an asynchronous run. |
| [`GET /v1/rags`](#ingestion-proxy) | List the RAGs of xat-rag. |
| [`GET /v1/rags/{rag}`](#ingestion-proxy) | Show the counts and the manifest of one RAG. |
| [`PUT /v1/rags/{rag}/documents`](#ingestion-proxy) | Upload documents to a RAG. |
| [`DELETE /v1/rags/{rag}/documents/{doc_id}`](#ingestion-proxy) | Delete one document from a RAG. |
| [`POST /v1/rags/{rag}/rebuild`](#ingestion-proxy) | Delete the index of a RAG and make an empty one. |
| [`GET /v1/jobs/{job_id}`](#ingestion-proxy) | Read an ingestion job of xat-rag. This is not an asynchronous run. |
| [`DELETE /v1/jobs/{job_id}`](#ingestion-proxy) | Ask an ingestion job of xat-rag to stop. |

Routes that do not exist, and routes with the wrong method, have no JSON body. They give 404 or 405 with an `Allow` header for a wrong method. The ingestion routes forward the request to xat-rag. See [Ingestion proxy](#ingestion-proxy).

### GET /health/live

Returns 200 when the process runs. It does not check the upstream services.

```bash
curl -s http://127.0.0.1:8430/health/live
```

```json
{"status":"live"}
```

### GET /health/ready

Calls `GET /health/ready` on each upstream service. Each call has a time limit of 3 seconds.

| `status` | Meaning | HTTP status |
|---|---|---|
| `ready` | All upstream services are ready. | 200 |
| `degraded` | An upstream service is down. The orchestrator still runs. | 200 |
| `unavailable` | A health check failed in an unexpected way. | 503 |

Each item in `upstreams` has `name`, `status` and `description`. The `status` of an item is `healthy` or `degraded`.

Example with no upstream service running (real output):

```bash
curl -s -i http://127.0.0.1:8430/health/ready
```

```json
{"status":"degraded","upstreams":[{"name":"xat-rag","status":"degraded","description":"xat-rag is not reachable."},{"name":"xat-llm-engine","status":"degraded","description":"xat-llm-engine is not reachable."}]}
```

Example with both upstream services ready. This example needs xat-rag and xat-llm-engine running:

```json
{"status":"ready","upstreams":[{"name":"xat-rag","status":"healthy","description":"xat-rag is ready."},{"name":"xat-llm-engine","status":"healthy","description":"xat-llm-engine is ready."}]}
```

### GET /v1/pipelines

Lists all pipelines, sorted by name. The list changes only after a restart. A new pipeline file appears after the orchestrator restarts.

| Field | Meaning |
|---|---|
| `pipelines` | The list of pipelines. Each item has the fields below. |
| `name` | The name of the pipeline. Callers use it in the run route. |
| `version` | The version of the pipeline file. |
| `summary` | A short text for a person. |
| `stream` | `allowed`, `never` or `required`. See [Pipeline files](#pipeline-files). |
| `input_schema` | The input schema of the pipeline. |
| `steps` | The step types of the pipeline, in order. |

```bash
curl -s http://127.0.0.1:8430/v1/pipelines
```

The response is an object with the key `pipelines`. This is the real item for the `chat` pipeline. The other items have the same fields:

```json
{"name":"chat","version":1,"summary":"Sends a prompt to the LLM with no retrieval.","stream":"allowed","input_schema":{"type":"object","required":["prompt"],"additionalProperties":false,"properties":{"prompt":{"type":"string","minLength":1,"maxLength":20000},"system_prompt":{"type":"string","maxLength":20000}}},"steps":["prompt.system","prompt.user","prompt.assemble","llm.generate"]}
```

### GET /v1/pipelines/{name}

Returns one pipeline, in the same form as an item of the list. An unknown name gives 404 `pipeline.not_found`.

```bash
curl -s http://127.0.0.1:8430/v1/pipelines/chat
```

```bash
curl -s -i http://127.0.0.1:8430/v1/pipelines/nope
```

```json
{"error":{"type":"not_found_error","message":"The pipeline does not exist.","code":"pipeline.not_found","run_id":null}}
```

### POST /v1/pipelines/{name}/runs

Runs a pipeline. The body has these fields:

| Field | Type | Default | Meaning |
|---|---|---|---|
| `input` | JSON value | `{}` | The input of the pipeline. The input schema of the pipeline checks it. |
| `stream` | boolean | `false` | Set `true` to receive server-sent events. See [Streamed runs](#streamed-runs). |
| `debug` | boolean | `false` | Set `true` to add the time of each step to the answer. |
| `webhook` | string | None | The address of a receiver for the result of an asynchronous run. Use it only with `mode=async`. See [Webhooks](#webhooks). |

An asynchronous run returns 202 with the run ID at once. See [Asynchronous runs](#asynchronous-runs). A sync run returns 200 with the run result when the run ends with an answer. The response has these fields:

| Field | Type | Meaning |
|---|---|---|
| `run_id` | string | The ID of the run. The same value is in the `X-Run-Id` header. |
| `pipeline` | string | The name of the pipeline. |
| `version` | integer | The version of the pipeline file. |
| `status` | string | `answered` when the model wrote the answer. `no_answer` when a step ended the run early, because it had no sources. |
| `answer.text` | string | The answer. |
| `answer.citations` | list | The sources that the answer cites. The list is empty when the answer has no citation. |
| `answer.citations[].number` | integer | The number of the source in the prompt. The answer uses it as `[1]`. |
| `answer.citations[].chunk_id` | string | The ID of the cited chunk. |
| `answer.citations[].title` | string | The title of the document. |
| `answer.citations[].uri` | string | The link to the document. The value is empty when the link has a scheme other than `http` or `https`. |
| `usage.prompt_tokens` | integer | The tokens that the LLM requests used. The value is an estimate in the orchestrator. |
| `usage.completion_tokens` | integer | The tokens that the answers used. |
| `warnings` | list of text | Notes about the run. A warning is not an error. |
| `debug` | object or null | The step times, when `debug` is `true`. Otherwise `null`. |
| `debug.steps[].id` | string | The ID of the step. |
| `debug.steps[].uses` | string | The step type. |
| `debug.steps[].duration_ms` | number | The time of the step in milliseconds. |

Example with xat-rag and xat-llm-engine running. The values depend on your data and model. This output comes from a test run with example data:

```bash
curl -s -X POST http://127.0.0.1:8430/v1/pipelines/rag-qa/runs \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Which port does the orders service use?"}, "debug": true}'
```

```json
{"run_id":"run_9ace3c5b0fd34d91a5d2","pipeline":"rag-qa","version":1,"status":"answered","answer":{"text":"The orders service uses port 8080 [1].","citations":[{"number":1,"chunk_id":"docs:orders.md#h/ports","title":"Orders","uri":"https://example.test/orders.md"}]},"usage":{"prompt_tokens":100,"completion_tokens":9},"warnings":[],"debug":{"steps":[{"id":"system","uses":"prompt.system","duration_ms":1.3},{"id":"user","uses":"prompt.user","duration_ms":0.2},{"id":"retrieve","uses":"rag.retrieve","duration_ms":4.2},{"id":"no-sources","uses":"flow.complete-if","duration_ms":0.2},{"id":"assemble","uses":"prompt.assemble","duration_ms":1.6},{"id":"generate","uses":"llm.generate","duration_ms":2.2},{"id":"citations","uses":"answer.verify-citations","duration_ms":1}]}}
```

Example of a `no_answer` result. xat-rag returned no chunks for the context, so the model was not called. This output comes from a test run with example data:

```json
{"run_id":"run_831aba7e6d0c4e6d96f7","pipeline":"rag-qa","version":1,"status":"no_answer","answer":{"text":"The sources hold no information for this question.","citations":[]},"usage":{"prompt_tokens":0,"completion_tokens":0},"warnings":[],"debug":null}
```

Example with no xat-llm-engine running. The `chat` pipeline calls the model, so the run fails (real output):

```bash
curl -s -i -X POST http://127.0.0.1:8430/v1/pipelines/chat/runs \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Hi"}}'
```

```http
HTTP/1.1 503 Service Unavailable
Retry-After: 5
X-Run-Id: run_fb9c7dd6e0df4048bbbe

{"error":{"type":"service_unavailable_error","message":"xat-llm-engine is not reachable.","code":"xat-llm-engine.unreachable","run_id":"run_fb9c7dd6e0df4048bbbe"}}
```

Example of a bad input (real output). The input has no `prompt` key:

```json
{"error":{"type":"invalid_request_error","message":"input.prompt: the property is required.","code":"input.invalid","run_id":"run_819c81dfdd1042c29862"}}
```

A run that failed on input has a `run_id`, because the run started before the input check. A request that fails before the run starts has no `run_id`. The `X-Run-Id` header is not set for these requests.

### POST /v1/ask

Runs the pipeline that `Api:DefaultPipeline` names. The default is `rag-qa`. The body and the response are the same as for `POST /v1/pipelines/{name}/runs`. The query parameter `mode` works in the same way.

```bash
curl -s -X POST http://127.0.0.1:8430/v1/ask \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Which port does the orders service use?"}}'
```

### GET /v1/steps

Lists the step types that pipeline files can use. The list has all built-in types, and any type that a module adds.

| Trait | Meaning |
|---|---|
| `CallsLlm` | The step calls xat-llm-engine. |
| `Streams` | The step sends `answer.delta` events when the caller streams. |
| `ProducesAnswer` | The step can end the run with an answer. |
| `Idempotent` | The engine can run the step again without harm. |

```bash
curl -s http://127.0.0.1:8430/v1/steps
```

```json
{"steps":[{"name":"answer.verify-citations","summary":"Maps the citations in the answer to the sources.","traits":[]},{"name":"flow.complete-if","summary":"Ends the run with a fixed answer when a condition is true.","traits":["ProducesAnswer"]},{"name":"llm.generate","summary":"Sends the messages to the LLM and stores the answer.","traits":["CallsLlm","Streams","ProducesAnswer"]},{"name":"prompt.append","summary":"Adds an instruction that the pipeline author wrote.","traits":[]},{"name":"prompt.assemble","summary":"Builds the chat messages and fits them into the token budget.","traits":[]},{"name":"prompt.context","summary":"Adds context text that the caller sent, in an escaped tag.","traits":[]},{"name":"prompt.system","summary":"Sets the system prompt from a text or a template.","traits":[]},{"name":"prompt.user","summary":"Sets the prompt of the caller.","traits":[]},{"name":"rag.retrieve","summary":"Gets the chunks of one xat-rag context for a question.","traits":["Idempotent"]},{"name":"retrieval.merge","summary":"Merges the chunks of several retrieval steps.","traits":["Idempotent"]}]}
```

The options of each built-in step are in [Built-in steps](#built-in-steps).

### GET /v1/contexts

Lists the retrieval aliases from `Retrieval:Profiles`. A pipeline uses an alias in the `profile` option of `rag.retrieve`.

| Field | Meaning |
|---|---|
| `alias` | The name that pipelines use. |
| `context` | The name of the xat-rag context. |
| `chunks` | The default number of chunks. |
| `available` | `true` when xat-rag lists the context. `false` when it does not. `null` when the call to xat-rag that lists the contexts fails or takes more than 2 seconds, for example because xat-rag is not reachable. |

```bash
curl -s http://127.0.0.1:8430/v1/contexts
```

With no xat-rag running (real output):

```json
{"contexts":[{"alias":"default","context":"ask-all","chunks":8,"available":null},{"alias":"diffs","context":"ask-diffs","chunks":5,"available":null},{"alias":"docs","context":"ask-docs","chunks":5,"available":null},{"alias":"jira","context":"ask-jira","chunks":5,"available":null}]}
```

This route calls xat-rag on each request. Each call is outside the rate limiter. The call to xat-rag has a time limit of 2 seconds. A slow xat-rag gives `available: null`, and the route still answers.

### GET /v1/models

Lists the pipelines that the OpenAI-compatible route can call, and the model aliases from `Llm:Models`. The fields `object` and `data` follow the OpenAI list of models. The fields `default_model` and `models` list the model aliases.

| Field | Meaning |
|---|---|
| `object` | Always `list`. |
| `data` | The pipelines that a plain chat request can call. The rules are below the table. |
| `data[].id` | The model name in the form `pipeline/<name>`. Send it as `model` in a chat request. |
| `data[].object` | Always `model`. |
| `data[].created` | Always `0`. |
| `data[].owned_by` | Always `xat-workflow-orchestrator`. |
| `default_model` | The alias that steps use when they name no model. |
| `models[].alias` | The name that pipelines use. |
| `models[].model` | The model ID in the form `provider/model`. |
| `models[].context_window` | The size of the context window in tokens. |
| `models[].supports_grammar` | The value of the setting. The orchestrator does not send grammars yet. |
| `models[].available` | `true` when xat-llm-engine lists the model. `false` when it does not. `null` when the call to xat-llm-engine that lists the models fails or takes more than 2 seconds, for example because xat-llm-engine is not reachable. |

A pipeline is listed in `data` when all of these rules hold:

- Its input schema has an input named `prompt`.
- It needs no required input, except `system_prompt`.
- Its stream mode is not `required`.

The call to xat-llm-engine has a time limit of 2 seconds. A slow xat-llm-engine gives `available: null`, and the route still answers.

```bash
curl -s http://127.0.0.1:8430/v1/models
```

With no xat-llm-engine running (real output). The `available` value is `null` in this case:

```json
{"object":"list","data":[{"id":"pipeline/chat","object":"model","created":0,"owned_by":"xat-workflow-orchestrator"},{"id":"pipeline/custom-prompt","object":"model","created":0,"owned_by":"xat-workflow-orchestrator"},{"id":"pipeline/rag-qa","object":"model","created":0,"owned_by":"xat-workflow-orchestrator"},{"id":"pipeline/rag-sequential","object":"model","created":0,"owned_by":"xat-workflow-orchestrator"}],"default_model":"default","models":[{"alias":"default","model":"local/ministral-3-3b","context_window":8192,"supports_grammar":true,"available":null}]}
```

## Streamed runs

Set `"stream": true` in the body to receive server-sent events. The pipeline must allow streams. A pipeline with `stream: never` gives 400 `stream.not_supported`. An asynchronous run does not stream. A request with `mode=async` and `"stream": true` gives 400 `stream.not_supported_async`.

```bash
curl -N -X POST http://127.0.0.1:8430/v1/pipelines/rag-qa/runs \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Which port does the orders service use?"}, "stream": true}'
```

The `-N` option turns off the buffer of curl, so each event shows at once.

The response has these headers: `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no` and `X-Run-Id`.

The HTTP status is 200 when the stream starts. A failure during the run comes as a `run.error` event. The HTTP status does not change. A failure before the run starts uses a normal HTTP error. Examples are a bad body, an unknown pipeline, a stream that the pipeline does not allow and a full admission queue.

An input error in a stream comes after `run.started`, as a `run.error` event.

Each event has an event name and a JSON payload:

| Event | Payload | When |
|---|---|---|
| `run.started` | `run_id`, `pipeline`, `version` | The first event. |
| `step.started` | `step_id`, `uses` | Before each step. |
| `answer.delta` | `text` | Each new part of the answer. Only steps that stream send it. |
| `answer.citations` | `citations`, a list of the same items as in a sync response | After the model writes the answer, when the prompt had sources. The list can be empty. |
| `run.completed` | The same body as a sync response | The last event of a successful run. |
| `run.error` | `error`, the same object as in an error body | The last event of a failed run. |

An example of a stream with xat-rag and xat-llm-engine running. This output comes from a test run with example data:

```text
event: run.started
data: {"run_id":"run_9374b5dc642a41299e6c","pipeline":"rag-qa","version":1}

event: step.started
data: {"step_id":"system","uses":"prompt.system"}

event: step.started
data: {"step_id":"user","uses":"prompt.user"}

event: step.started
data: {"step_id":"retrieve","uses":"rag.retrieve"}

event: step.started
data: {"step_id":"no-sources","uses":"flow.complete-if"}

event: step.started
data: {"step_id":"assemble","uses":"prompt.assemble"}

event: step.started
data: {"step_id":"generate","uses":"llm.generate"}

event: answer.delta
data: {"text":"The orders service uses port 8080"}

event: answer.delta
data: {"text":" [1]."}

event: step.started
data: {"step_id":"citations","uses":"answer.verify-citations"}

event: answer.citations
data: {"citations":[{"number":1,"chunk_id":"docs:orders.md#h/ports","title":"Orders","uri":"https://example.test/orders.md"}]}

event: run.completed
data: {"run_id":"run_9374b5dc642a41299e6c","pipeline":"rag-qa","version":1,"status":"answered","answer":{"text":"The orders service uses port 8080 [1].","citations":[{"number":1,"chunk_id":"docs:orders.md#h/ports","title":"Orders","uri":"https://example.test/orders.md"}]},"usage":{"prompt_tokens":100,"completion_tokens":9},"warnings":[],"debug":null}
```

An example of a stream with no xat-rag running (real output, abbreviated):

```text
event: run.started
data: {"run_id":"run_a0b80665f3444512a745","pipeline":"rag-qa","version":1}

event: step.started
data: {"step_id":"system","uses":"prompt.system"}

event: step.started
data: {"step_id":"user","uses":"prompt.user"}

event: step.started
data: {"step_id":"retrieve","uses":"rag.retrieve"}

event: run.error
data: {"error":{"type":"service_unavailable_error","message":"xat-rag is not reachable.","code":"xat-rag.unreachable","run_id":"run_a0b80665f3444512a745"}}
```

When the caller closes the connection, the run is cancelled. The cancel also stops an upstream call that is running.

## Asynchronous runs

An asynchronous run starts in the background. The caller gets a run ID at once. The caller then reads the state of the run, or receives the result as a webhook. See [Webhooks](#webhooks).

To start an asynchronous run, add `?mode=async` to a run route. The route returns 202 Accepted. The run waits in a queue until a worker takes it. The orchestrator runs the pipeline in the same way as a sync run.

The orchestrator keeps the runs in memory. A restart removes all runs. See [Retention](#retention).

### Submit a run

Send the same body as a sync run. The submit call does not call the upstream services. The run calls them later.

```bash
curl -s -i -X POST 'http://127.0.0.1:8430/v1/pipelines/rag-qa/runs?mode=async' \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Which port does the orders service use?"}}'
```

Real output. Some headers are removed.

```http
HTTP/1.1 202 Accepted
Content-Type: application/json
Location: /v1/runs/run_857a5e1cfb0143e99eaa
X-Run-Id: run_857a5e1cfb0143e99eaa

{"run_id":"run_857a5e1cfb0143e99eaa","status":"queued","status_url":"/v1/runs/run_857a5e1cfb0143e99eaa"}
```

The 202 response has these fields:

| Field | Type | Meaning |
|---|---|---|
| `run_id` | string | The ID of the run. The same value is in the `X-Run-Id` header. |
| `status` | string | Always `queued` in this response. |
| `status_url` | string | The path to read the run. The same path is in the `Location` header. |

The orchestrator checks the input and the webhook address before it accepts the run. A bad input gets 400 `input.invalid`. A bad webhook address gets 400 with a code that starts with `webhook.`. In both cases, no run is stored. When the queue is full, the response is 503 `jobs.queue_full` with `Retry-After: 5`.

Example of a full queue (real output):

```http
HTTP/1.1 503 Service Unavailable
Retry-After: 5

{"error":{"type":"overloaded_error","message":"The queue of asynchronous runs is full. Try again later.","code":"jobs.queue_full","run_id":null}}
```

### Read a run

Send `GET /v1/runs/{id}` to read the state of a run. The response has these fields:

| Field | Type | Meaning |
|---|---|---|
| `run_id` | string | The ID of the run. |
| `pipeline` | string | The name of the pipeline. |
| `version` | integer | The version of the pipeline file. |
| `status` | string | The state of the run. See [States](#states). |
| `created_at` | string | The time at which the orchestrator accepted the run. The value is a date and time with an offset. |
| `started_at` | string or null | The time at which a worker started the run. The value is `null` while the run waits. |
| `completed_at` | string or null | The time at which the run ended. The value is `null` until then. |
| `result` | object or null | The result of a `succeeded` run. It has the same form as a sync response. Otherwise `null`. |
| `error` | object or null | The error of a `failed` or `cancelled` run. It has the fields `type`, `message`, `code` and `run_id`. Otherwise `null`. |
| `webhook` | object or null | The state of the webhook. The value is `null` when the run has no webhook. See [Delivery state](#delivery-state). |

This example needs xat-rag and xat-llm-engine running. The values depend on your data and model. This output comes from a test run with example data:

```bash
curl -s http://127.0.0.1:8430/v1/runs/run_3cd46f513abd4f1b9023
```

```json
{"run_id":"run_3cd46f513abd4f1b9023","pipeline":"rag-qa","version":1,"status":"succeeded","created_at":"2026-11-11T11:23:20.961894+00:00","started_at":"2026-11-11T11:23:20.961917+00:00","completed_at":"2026-11-11T11:23:20.963131+00:00","result":{"run_id":"run_3cd46f513abd4f1b9023","pipeline":"rag-qa","version":1,"status":"answered","answer":{"text":"The orders service uses port 8080 [1].","citations":[{"number":1,"chunk_id":"docs:orders.md#h/ports","title":"Orders","uri":"https://example.test/orders.md"}]},"usage":{"prompt_tokens":100,"completion_tokens":9},"warnings":[],"debug":null},"error":null,"webhook":null}
```

Example of a failed run. The `context` input names an alias that is not configured (real output):

```json
{"run_id":"run_c57254e03d07480c85ae","pipeline":"rag-qa","version":1,"status":"failed","created_at":"2026-11-11T11:23:22.025308+00:00","started_at":"2026-11-11T11:23:22.025346+00:00","completed_at":"2026-11-11T11:23:22.025925+00:00","result":null,"error":{"type":"invalid_request_error","message":"The retrieval profile is not configured. Use GET /v1/contexts to list the profiles.","code":"retrieve.profile_unknown","run_id":"run_c57254e03d07480c85ae"},"webhook":null}
```

### States

| State | Meaning |
|---|---|
| `queued` | The run waits for a worker. A cancel changes it to `cancelled` at once. |
| `running` | A worker executes the run. |
| `succeeded` | The run ended with a result. A `no_answer` result is a `succeeded` run. Read `result.status` to see it. |
| `failed` | The run ended with an error. The `error` field shows the error. |
| `cancelled` | The caller cancelled the run. The `error` field has the code `run.cancelled`. |

The state diagram is in [architecture](architecture.md#asynchronous-runs-and-webhooks).

### Cancel a run

Send `DELETE /v1/runs/{id}` to cancel a run. The answer shows the state at the moment of the request:

- A queued run changes to `cancelled` at once.
- A running run stops soon after. The answer can still show `queued` or `running`. Read the run again to see the final state.
- A finished run does not change. The answer shows its final state.
- An unknown run gives 404 `run.not_found`.

Example of a queued run that the caller cancels (real output):

```json
{"run_id":"run_79241d4e3ff54175833c","pipeline":"rag-qa","version":1,"status":"cancelled","created_at":"2026-11-11T11:21:36.711307+00:00","started_at":null,"completed_at":"2026-11-11T11:21:36.712017+00:00","result":null,"error":{"type":"cancelled_error","message":"The caller cancelled the run.","code":"run.cancelled","run_id":"run_79241d4e3ff54175833c"},"webhook":null}
```

### Retention

The store keeps finished runs for a limited time. These rules apply:

- A finished run is removed after `Jobs:RetentionMinutes`.
- When there are more finished runs than `Jobs:RetainFinished`, the oldest finished runs are removed.
- A caller that reads a finished run after its retention time gets 404 `run.not_found`.
- Queued and running runs are never removed.
- A restart removes all runs.

Example after a restart or after the retention time (real output):

```http
HTTP/1.1 404 Not Found

{"error":{"type":"not_found_error","message":"The run does not exist, or the service removed it.","code":"run.not_found","run_id":null}}
```

## Webhooks

A webhook sends a finished run to a receiver. The orchestrator sends it once, when the run reaches `succeeded`, `failed` or `cancelled`. A run that the caller cancels while it waits also sends a webhook.

Set the address in the `webhook` field of the body. The field works only with `mode=async`. The receiver script in [Run in the background](quickstart.md#run-in-the-background) helps you test this. This example needs xat-rag and xat-llm-engine running:

```bash
curl -s -X POST 'http://127.0.0.1:8430/v1/pipelines/rag-qa/runs?mode=async' \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Which port does the orders service use?"}, "webhook": "http://127.0.0.1:9000/hook"}'
```

### Request

The orchestrator sends a `POST` request with a JSON body. The request has these headers:

- `Content-Type: application/json; charset=utf-8`
- `X-Run-Id`, which holds the ID of the run
- `X-Webhook-Event: run.finished`

The body has the event name and the run. The `run` object has the same form as the response of `GET /v1/runs/{id}`. In the body, the `webhook` state is `pending`, and `attempts` is 0. The final delivery state is in the `GET` response. This is a real body from a test run:

```json
{"event":"run.finished","run":{"run_id":"run_6e0f8016874b47c18b9e","pipeline":"rag-qa","version":1,"status":"succeeded","created_at":"2026-11-11T11:20:44.413381+00:00","started_at":"2026-11-11T11:20:44.41346+00:00","completed_at":"2026-11-11T11:20:44.416172+00:00","result":{"run_id":"run_6e0f8016874b47c18b9e","pipeline":"rag-qa","version":1,"status":"answered","answer":{"text":"The orders service uses port 8080 [1].","citations":[{"number":1,"chunk_id":"docs:orders.md#h/ports","title":"Orders","uri":"https://example.test/orders.md"}]},"usage":{"prompt_tokens":100,"completion_tokens":9},"warnings":[],"debug":null},"error":null,"webhook":{"url":"http://127.0.0.1:9401","status":"pending","attempts":0,"error":null}}}
```

### Delivery

The orchestrator uses these rules to send a webhook:

- It retries a try after a connection error, a timeout, status 408, status 429 or a 5xx status.
- Another status, for example 400 or 302, ends the delivery at once.
- `Webhooks:RetryCount` sets the number of retries. The number of tries is the retry count plus one.
- `Webhooks:RetryDelayMilliseconds` sets the delay before the first retry. The delay doubles with each retry. It has a random variation, and it stops growing at 30 seconds.
- `Webhooks:TimeoutSeconds` sets the time limit of one try.
- The sender never follows a redirect. A redirect ends the delivery as failed.
- Delivery has its own workers. `Jobs:WebhookWorkers` sets their number. A slow receiver does not block the runs.

### Delivery state

The result of the delivery is in the `webhook` field of the `GET` response:

| Field | Type | Meaning |
|---|---|---|
| `url` | string | The address with the scheme, the host and the port only. The path and the query are not shown. |
| `status` | string | `pending` before the delivery ends, `delivered` when the receiver accepted the webhook, or `failed` when the delivery did not succeed. |
| `attempts` | integer | The number of tries. |
| `error` | string or null | The reason for a failed delivery. Otherwise `null`. |

The `error` field has one of these texts:

- `The receiver refused the webhook with status 404.`
- `The receiver still answered with status 503 after the retries.`
- `The receiver is not reachable.`
- `The receiver did not answer in time.`
- `The delivery queue is full.`
- `The delivery failed because of an internal error.`
- `The service stopped before the delivery ended.`

A failed delivery does not change the run result. The run keeps its state and its result or error. Example of a failed delivery (real output, the receiver could not be reached). This is the `webhook` field of a `GET` response:

```json
{"url":"https://hooks.example:8443","status":"failed","attempts":4,"error":"The receiver is not reachable."}
```

### Receiver

Your receiver should follow these rules:

- Answer with a 2xx status at once. Process the body after the answer. A slow answer can cause a timeout and a repeat.
- Ignore a repeat. Delivery is at least once, so the same webhook can arrive more than once. Use the `X-Run-Id` header to find a repeat.
- Do not rely on the webhook as proof. Webhooks have no signature and no authentication. A receiver cannot prove that the orchestrator sent a webhook.

There is no way to send a webhook again. A failed webhook stays failed.

### Address rule

The orchestrator checks the address when the run is submitted. A bad address gets 400, and no run is stored. The rule has these parts:

- The address must be absolute, and it must use `http` or `https`. Other addresses give 400 `webhook.invalid_url`.
- The address must not have a user name or a password. Such an address gives 400 `webhook.invalid_url`.
- The host must be a loopback host (`127.0.0.1`, `localhost` or `::1`), or it must be in `Webhooks:AllowedHosts`. Another host gives 400 `webhook.host_not_allowed`.
- A host name in `Webhooks:AllowedHosts` ignores case. Any port is allowed.
- The value `*` in `Webhooks:AllowedHosts` allows every host.

Warning: the value `*` lets a caller send requests to any address that the machine can reach. Use it only in a test.

This rule limits where the orchestrator sends requests. It is not authentication. The error messages never repeat the address.

The orchestrator sends the full address to the receiver, with its query. It stores and shows only the scheme, the host and the port. The full address stays in memory until the delivery ends. See [Allow another webhook host](quickstart.md#allow-another-webhook-host) to change the list.

## OpenAI-compatible API

The route `POST /v1/chat/completions` lets a program use an OpenAI library with a pipeline. The model name is `pipeline/<name>`. The route changes the request and the answer to the OpenAI format. It runs the same engine as the other run routes. It does not call the OpenAI service.

The route has no authentication. It accepts any API key and ignores the `Authorization` header. See [Security](#security).

### Callable pipelines

A pipeline can be called through this route when its input schema has an input named `prompt`. All shipped pipelines have this input.

A pipeline without this input gives 400 `request.pipeline_not_chat`. Call such a pipeline with [`POST /v1/pipelines/{name}/runs`](#post-v1pipelinesnameruns).

[`GET /v1/models`](#get-v1models) lists the pipelines that a plain chat request can call. A request can call other pipelines too, when `xat.input` gives their other required inputs.

### Request fields

The body has these fields:

| Field | Meaning |
|---|---|
| `model` | Required. The form is `pipeline/<name>`. A missing value gives 400 `request.model_missing`. A value without the prefix gives 400 `request.model_invalid`. An unknown name gives 404 `model.not_found`. |
| `messages` | Required. A list with at least one message. Each message has a `role` and a `content`. |
| `stream` | Set `true` to receive a stream. The stream mode of the pipeline applies. A value that is not `true`, `false` or `null` gives 400 `request.invalid_field`. See [Streamed responses](#streamed-responses). |
| `stream_options` | Set `include_usage` to `true` to receive a usage piece at the end of a stream. A value that is not an object or `null` gives 400 `request.invalid_field`. |
| `xat` | An object with the extension fields. See [Extension fields](#extension-fields). |

#### Message mapping

The route maps the messages to the inputs of the pipeline:

| Messages | Use in the pipeline |
|---|---|
| The last message with the role `user` | The input `prompt`. |
| All messages with the role `system` or `developer` | Joined with a blank line into the input `system_prompt`. The pipeline must declare this input. Otherwise the route adds a note to `warnings`. |
| Earlier messages with the role `user` or `assistant` | Not used. The route adds a note to `warnings` that gives the number of these messages. |

The allowed roles are these:

- `system`
- `developer`
- `user`
- `assistant`

Another role gives 400 `request.unsupported_role`. A message without a role gives 400 `request.message_invalid`.

The `content` field is a text, or a list of parts. Each part must have the type `text`. The route joins the texts of the parts with a newline. Another part type, such as an image, gives 400 `request.unsupported_content`.

#### Extension fields

The `xat` object carries the values that the OpenAI format has no place for. The route uses these fields:

- `xat.input` is an object. Each key is an input of the pipeline, for example `context` or `language`. The values must match the input schema, as in any run.
- `xat.debug` set to `true` adds the step times to the response.

The messages win for `prompt`. The route always sets `prompt` from the last user message. For `system_prompt`, the messages win only when the request has system or developer messages. Otherwise the value in `xat.input` is used, if the request sets one. Other values in `xat.input` keep their values.

```json
{
  "model": "pipeline/custom-prompt",
  "messages": [
    {"role": "system", "content": "You are support."},
    {"role": "user", "content": "Which port?"}
  ],
  "xat": {"input": {"context": "Customer uses v2.", "language": "German"}, "debug": true}
}
```

#### Fields that the route ignores

The route accepts these fields and ignores them. The route adds one note to `warnings` for the fields that the request sets to a value other than `null`. The pipeline sets these values:

- `temperature`
- `max_tokens`
- `max_completion_tokens`
- `top_p`
- `frequency_penalty`
- `presence_penalty`
- `seed`
- `stop`
- `user`
- `logit_bias`
- `logprobs`
- `top_logprobs`
- `store`
- `metadata`
- `service_tier`
- `reasoning_effort`

#### Fields that the route refuses

The route refuses these fields with 400 `request.unsupported_field`. A field with the value `null` is accepted.

- `tools`
- `tool_choice`
- `functions`
- `function_call`
- `audio`
- `modalities`
- `prediction`
- `parallel_tool_calls`
- `n`, when the value is not `1`
- `response_format`, when the type is not `text`

Pipelines do not call tools, and they do not return structured output yet.

### Response fields

A request without `stream` gets 200 and a `chat.completion` object. The response has the content type `application/json`. The object has these fields:

| Field | Meaning |
|---|---|
| `id` | The text `chatcmpl-` followed by the run ID. |
| `object` | Always `chat.completion`. |
| `created` | The time at which the route accepted the request, in Unix seconds. |
| `model` | The model name from the request. |
| `choices` | A list with one item. The item has `index` `0`, a `message` with the role `assistant` and the answer, and `finish_reason` `stop`. |
| `usage` | `prompt_tokens`, `completion_tokens` and `total_tokens`. The values are the sums for the whole run. |
| `xat` | The extension object. See below. |

The `xat` object has these fields:

| Field | Meaning |
|---|---|
| `run_id` | The ID of the run. |
| `pipeline` | The name of the pipeline. |
| `status` | `answered`, or `no_answer` when a step ended the run without an answer. |
| `citations` | The sources that the answer cites. The items have the same form as in the native response. |
| `warnings` | The notes about the mapping first, then the notes about the run. An empty list means no note. |
| `debug` | Only when `xat.debug` is `true`. An object with the list `steps`. Each item has `id`, `uses` and `duration_ms`. |

The header `X-Run-Id` holds the run ID when the run started.

Example with xat-rag and xat-llm-engine running. The values depend on your data and model. This output comes from a test run with example data:

```json
{"id":"chatcmpl-run_1ba18c4431a04fa0b0a1","object":"chat.completion","created":1794397421,"model":"pipeline/rag-qa","choices":[{"index":0,"message":{"role":"assistant","content":"The orders service uses port 8080 [1]."},"finish_reason":"stop"}],"usage":{"prompt_tokens":100,"completion_tokens":9,"total_tokens":109},"xat":{"run_id":"run_1ba18c4431a04fa0b0a1","pipeline":"rag-qa","status":"answered","citations":[{"number":1,"chunk_id":"docs:orders.md#h/ports","title":"Orders","uri":"https://example.test/orders.md"}],"warnings":[]}}
```

A request with notes in `warnings` looks like this. The request has two earlier messages, and it sets two fields that the route ignores. This is the `xat` object of a real response:

```json
"xat":{"run_id":"run_97ab8ea56d4845d3b188","pipeline":"chat","status":"answered","citations":[],"warnings":["2 earlier message(s) were not used. The pipeline answers the last user message only.","The fields temperature, max_tokens were not used. The pipeline sets them."]}
```

### Streamed responses

Set `stream` to `true`. The response has the content type `text/event-stream`. The response has only `data:` lines. It has no `event:` lines. The pieces come in this order:

- A first piece with the role `assistant` and an empty `content`.
- One piece for each new part of the answer. The text is in `delta.content`.
- A finish piece with `finish_reason` `stop` and the `xat` object.
- A usage piece with an empty `choices` list. The route sends it only when `stream_options.include_usage` is `true`.
- The line `data: [DONE]`.

The route sends the status 200 when the first piece is ready. An error before this point is a normal HTTP error. It has the status of the error and the error body. An error after this point is one `data:` line with the error body. The stream then has no `data: [DONE]` line.

If the event channel of the run closes with no last event, the route sends the error `run.stream_lost`. It has the status 500 and the type `internal_error`. Before the first piece, this is a normal HTTP error. After it, this is a `data:` line, as described above.

A step that ends the run without streaming, for example the no-answer path, sends its text as one piece.

When the caller disconnects, the run is cancelled. The cancel also stops an upstream call that is running.

Example with xat-rag and xat-llm-engine running. This output comes from a test run with example data:

```text
data: {"id":"chatcmpl-run_64d55a9a7e1346088b33","object":"chat.completion.chunk","created":1794397427,"model":"pipeline/rag-qa","choices":[{"index":0,"delta":{"role":"assistant","content":""},"finish_reason":null}]}

data: {"id":"chatcmpl-run_64d55a9a7e1346088b33","object":"chat.completion.chunk","created":1794397427,"model":"pipeline/rag-qa","choices":[{"index":0,"delta":{"content":"The orders service uses port 8080"},"finish_reason":null}]}

data: {"id":"chatcmpl-run_64d55a9a7e1346088b33","object":"chat.completion.chunk","created":1794397427,"model":"pipeline/rag-qa","choices":[{"index":0,"delta":{"content":" [1]."},"finish_reason":null}]}

data: {"id":"chatcmpl-run_64d55a9a7e1346088b33","object":"chat.completion.chunk","created":1794397427,"model":"pipeline/rag-qa","choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"xat":{"run_id":"run_64d55a9a7e1346088b33","pipeline":"rag-qa","status":"answered","citations":[{"number":1,"chunk_id":"docs:orders.md#h/ports","title":"Orders","uri":"https://example.test/orders.md"}],"warnings":[]}}

data: {"id":"chatcmpl-run_64d55a9a7e1346088b33","object":"chat.completion.chunk","created":1794397427,"model":"pipeline/rag-qa","choices":[],"usage":{"prompt_tokens":100,"completion_tokens":9,"total_tokens":109}}

data: [DONE]
```

The stream mode of the pipeline applies. A pipeline with `stream: never` gives 400 `stream.not_supported` when `stream` is `true`. A pipeline with `stream: required` gives 400 `stream.required` when `stream` is not `true`.

### OpenAI-compatible errors

Errors have the OpenAI form:

```json
{"error":{"message":"The model does not exist. GET /v1/models lists the names.","type":"not_found_error","param":null,"code":"model.not_found"}}
```

The `type` and the HTTP status are the same as in the other routes. The `param` field is always `null`. The error body has no `run_id` field. The header `X-Run-Id` holds the run ID when the run started. A 503 has a `Retry-After` header.

These codes are specific to this route:

| Code | Status | Cause |
|---|---|---|
| `request.invalid_json` | 400 | The body is not JSON, or it is not a JSON object. |
| `request.content_type` | 400 | The content type is not `application/json`. |
| `request.model_missing` | 400 | The field `model` is missing, or it is not a text. |
| `request.model_invalid` | 400 | The model does not start with `pipeline/`. |
| `model.not_found` | 404 | No pipeline has this name. |
| `request.messages_missing` | 400 | The field `messages` is missing, or the list is empty. |
| `request.message_invalid` | 400 | A message is not an object, or it has no role. |
| `request.unsupported_role` | 400 | The role is not `system`, `developer`, `user` or `assistant`. |
| `request.unsupported_content` | 400 | A content part has a type other than `text`. |
| `request.no_user_message` | 400 | No message has the role `user`. |
| `request.pipeline_not_chat` | 400 | The pipeline has no input named `prompt`. |
| `request.unsupported_field` | 400 | The request sets a refused field. |
| `request.invalid_field` | 400 | The field `stream` is not `true`, `false` or `null`, or the field `stream_options` is not an object or `null`. |
| `stream.not_supported` | 400 | The pipeline has `stream: never`, and the request sets `stream` to `true`. |
| `stream.required` | 400 | The pipeline has `stream: required`, and the request does not set `stream` to `true`. |
| `input.invalid` | 400 | The input does not match the input schema. For example, the last user message is empty. |
| `admission.full` | 503 | All run slots and all queue places are in use. |
| `run.stream_lost` | 500 | The event channel of the run closed before the run ended. See [Streamed responses](#streamed-responses). |

Errors from a run, such as `xat-rag.unreachable`, have the same codes and statuses as in the [errors](#errors) table.

### Example with curl

This example needs xat-rag and xat-llm-engine running:

```bash
curl -s -X POST http://127.0.0.1:8430/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model": "pipeline/rag-qa", "messages": [{"role": "user", "content": "Which port does the orders service use?"}]}'
```

For a stream, add `"stream": true` and use the `-N` option of curl:

```bash
curl -N -X POST http://127.0.0.1:8430/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model": "pipeline/rag-qa", "stream": true, "stream_options": {"include_usage": true}, "messages": [{"role": "user", "content": "Which port does the orders service use?"}]}'
```

### Example with the official OpenAI Python library

Install the `openai` package in a virtual environment:

```bash
python3 -m venv .venv
.venv/bin/pip install openai
```

Then run this script. It needs xat-rag and xat-llm-engine running:

```python
from openai import OpenAI

client = OpenAI(base_url="http://127.0.0.1:8430/v1", api_key="anything")

completion = client.chat.completions.create(
    model="pipeline/rag-qa",
    messages=[{"role": "user", "content": "Which port does the orders service use?"}],
)
print(completion.choices[0].message.content)
print(completion.model_extra["xat"]["run_id"])
```

The API key can have any value, because the orchestrator does not check it. The output of this script is the answer, followed by the run ID. The library puts the `xat` object in `model_extra`. This is the real output of the script:

```text
The orders service uses port 8080 [1].
run_daebd1ea619e47639b88
```

This example streams the answer:

```python
from openai import OpenAI

client = OpenAI(base_url="http://127.0.0.1:8430/v1", api_key="anything")

stream = client.chat.completions.create(
    model="pipeline/rag-qa",
    messages=[{"role": "user", "content": "Which port does the orders service use?"}],
    stream=True,
)
for chunk in stream:
    if chunk.choices and chunk.choices[0].delta.content:
        print(chunk.choices[0].delta.content, end="", flush=True)
print()
```

The route was tested with the official `openai` package for Python.

### Known limits of this route

- The route uses only the last user message. Earlier turns are not used. The `warnings` field says so.
- The route has no tools, no structured output and no more than one choice.
- The `usage` values are the sums for the whole run. They are not the values of one model call.
- The system and developer messages go to `system_prompt`. The assemble step does not escape this text, as for the native `system_prompt` input. See [Security](#security).

## Ingestion proxy

The ingestion proxy forwards seven routes of xat-rag. A caller uses one address for runs and for ingestion. The proxy sends the request to xat-rag, and it returns the answer of xat-rag. The proxy does not check the content of an upload.

Warning: The ingestion proxy has no authentication and no authorization. This is a decision of the project owner. Any program on the same machine can upload documents, delete documents and rebuild a RAG. The routes `delete-document` and `rebuild-rag` are on by default. To turn a route off, see [Turn off a route](#turn-off-a-route).

### Route names and paths

| Route name | Method and path | Purpose |
|---|---|---|
| `list-rags` | `GET /v1/rags` | List the RAGs and the contexts. |
| `get-rag` | `GET /v1/rags/{rag}` | Show the counts and the manifest of one RAG. |
| `upload-documents` | `PUT /v1/rags/{rag}/documents` | Upload documents as a multipart body. xat-rag starts an ingestion job. |
| `delete-document` | `DELETE /v1/rags/{rag}/documents/{doc_id}` | Delete one document. |
| `rebuild-rag` | `POST /v1/rags/{rag}/rebuild` | Delete the index of a RAG and make an empty one. |
| `get-ingestion-job` | `GET /v1/jobs/{job_id}` | Read an ingestion job of xat-rag. |
| `cancel-ingestion-job` | `DELETE /v1/jobs/{job_id}` | Ask an ingestion job of xat-rag to stop. |

The route name is the value for `Ingestion:DisabledRoutes`. See [Settings](#ingestion).

The proxy forwards no other path. A path that is not in this table gives 404 with an empty body, and xat-rag gets no request. Examples are `POST /v1/context`, `POST /v1/query/build` and the debug search, for example `POST /v1/rags/docs/search`. A wrong method gives 405.

### Jobs and asynchronous runs

The route `GET /v1/jobs/{job_id}` reads an ingestion job of xat-rag. It does not read an asynchronous run of the orchestrator. The two kinds of work are different:

| | Ingestion job | Asynchronous run |
|---|---|---|
| Where it runs | xat-rag | The orchestrator |
| How it starts | `PUT /v1/rags/{rag}/documents` | A run route with `?mode=async` |
| ID | Set by xat-rag | Starts with `run_` |
| Read it with | `GET /v1/jobs/{job_id}` | [`GET /v1/runs/{id}`](#read-a-run) |
| Stop it with | `DELETE /v1/jobs/{job_id}` | [`DELETE /v1/runs/{id}`](#cancel-a-run) |

Do not send a run ID to `/v1/jobs/{job_id}`. The job route forwards the ID to xat-rag, and xat-rag answers for it.

### Request rules

- The RAG name must match `[a-z0-9][a-z0-9-]{0,63}`. The match is case-sensitive. For example, `DOCS` gives 404, and xat-rag gets no request.
- The job ID must match `[A-Za-z0-9_-]{1,64}`.
- The proxy sends the method, the path, the query and the body to xat-rag.
- A path that ends with a slash gives 404 `ingestion.route_not_found` with the message `The path must not end with a slash.`. xat-rag gets no request.

### Document IDs

The proxy sends the document ID of `DELETE /v1/rags/{rag}/documents/{doc_id}` to xat-rag as the caller wrote it. A slash in the ID must be sent as `%2F`, and xat-rag gets the same `%2F`. An encoded percent sign stays encoded. For example, the ID `docs:sub/orders.md` is sent as `docs%3Asub%2Forders.md`.

An ID that has a dot segment is refused. A dot segment is `.` or `..`. The check also works when the dots or the separators are encoded. The proxy answers 404 `ingestion.route_not_found` with the message `The document ID is not valid.`, and xat-rag gets no request. Real output for the ID `x%2F..%2Fy`:

```bash
curl -s -i -X DELETE 'http://127.0.0.1:8430/v1/rags/docs/documents/x%2F..%2Fy'
```

```http
HTTP/1.1 404 Not Found
Content-Type: application/json

{"error":{"type":"not_found_error","message":"The document ID is not valid.","code":"ingestion.route_not_found","run_id":null}}
```

The server normalizes a plain `.` or `..` segment in the path before the proxy reads it. For this reason, a request with a plain `..` can give 405, and a request with a plain `.` can give the slash message.

### Uploads

The proxy streams the upload to xat-rag. It does not keep the upload in memory. The limit for an upload is `Ingestion:MaxUploadBytes`. The default is 64 MiB. The other routes keep the limit `Api:MaxRequestBodyBytes`.

xat-rag has its own limits. It refuses an upload above 64 MiB, and it refuses more than 256 documents in one upload. These limits still apply. A higher `Ingestion:MaxUploadBytes` does not allow a larger upload than xat-rag accepts.

When the request declares a size above the limit, the proxy answers 413 before it contacts xat-rag. Real output, with some headers removed:

```http
HTTP/1.1 413 Payload Too Large
Content-Type: application/json

{"error":{"type":"invalid_request_error","message":"The upload is larger than the limit.","code":"request.too_large","run_id":null}}
```

When the request has no declared size, for example a chunked upload, the server counts the bytes while it reads them. Above the limit, the server stops the read. The caller gets the same 413 error, and the connection closes. xat-rag receives an upload that ends early. Real output for a chunked upload above the limit, with some headers removed:

```http
HTTP/1.1 413 Payload Too Large
Connection: close
Content-Type: application/json

{"error":{"type":"invalid_request_error","message":"The upload is larger than the limit.","code":"request.too_large","run_id":null}}
```

### Headers

The proxy sends only these request headers to xat-rag:

- `Accept`
- `Accept-Encoding`
- `User-Agent`
- The body headers, such as `Content-Type` and `Content-Length`.

Every other header of the caller is not sent. Examples are these:

- `Authorization`
- `Cookie`
- `Referer`
- `X-Forwarded-For`
- `X-HTTP-Method-Override`
- A custom header

The proxy sets the `Host` header to the address of xat-rag. The proxy does not send the trace headers `traceparent`, `tracestate` and `baggage`, so a trace of the caller does not continue into xat-rag.

The answer of xat-rag, with its status, headers and body, goes to the caller as it is. This includes the error answers of xat-rag.

### Errors of the proxy

| Status | Code | When |
|---|---|---|
| 404 | `ingestion.route_disabled` | The route is in `Ingestion:DisabledRoutes`, or `Ingestion:Enabled` is `false`. |
| 404 | `ingestion.route_not_found` | The path ends with a slash, or a document ID has a dot segment. xat-rag gets no request. |
| 404 | None | The path is not a route of the proxy. The body is empty. |
| 405 | None | The method is not the method of the route. |
| 413 | `request.too_large` | The upload is above `Ingestion:MaxUploadBytes`. The size is declared, or the server counts it while it reads the upload. |
| 502 | `xat-rag.proxy_failed` | The proxy cannot read the answer of xat-rag, or it failed for another reason. |
| 503 | `xat-rag.unreachable` | xat-rag is not reachable. Only a failure to connect gives this code. The `Retry-After` value is 5 seconds. |
| 504 | `xat-rag.timeout` | No data moves between the caller and xat-rag for `Ingestion:ActivityTimeoutSeconds`. |
| Any | The code of xat-rag | xat-rag answered with an error. The proxy passes the answer as it is. |

Real output when xat-rag is not reachable:

```http
HTTP/1.1 503 Service Unavailable
Retry-After: 5

{"error":{"type":"service_unavailable_error","message":"xat-rag is not reachable.","code":"xat-rag.unreachable","run_id":null}}
```

The proxy does not retry a request. The address of xat-rag comes from `Upstreams:XatRag`. The proxy reads the address again after a failed connection.

If xat-rag starts an answer and then fails, the proxy cannot send an error. The caller gets an incomplete answer.

An answer that the proxy cannot read gives 502 `xat-rag.proxy_failed`. Real output, for an answer from xat-rag with a malformed header line:

```http
HTTP/1.1 502 Bad Gateway
Content-Type: application/json

{"error":{"type":"upstream_error","message":"The request to xat-rag failed.","code":"xat-rag.proxy_failed","run_id":null}}
```

### Turn off a route

Warning: A route that is turned off gives 404 to every caller of the orchestrator. The upstream services have no authentication. A program on the same machine can still call xat-rag directly. Turning off a route does not protect the data in xat-rag.

1. Open `src/Xat.Orchestrator.Api/appsettings.json`.
2. Add the route names to the `DisabledRoutes` list in the `Ingestion` section. This example turns off the two routes that delete data:

    ```json
    "DisabledRoutes": [ "delete-document", "rebuild-rag" ]
    ```

3. Restart the orchestrator. A route name that does not exist stops the start.
4. Check the route. It gives 404 with the code `ingestion.route_disabled`.

You can also pass a name on the command line, for example `--Ingestion:DisabledRoutes:0=delete-document`. To turn off the whole proxy, set `Ingestion:Enabled` to `false`.

## Pipeline files

A pipeline is a YAML file in the folder that `Pipelines:Folder` names. The file extension is `.yaml` or `.yml`. A relative folder starts at the folder of the orchestrator program, not at the current folder of the shell.

The orchestrator reads all files at start. A file that is not valid stops the start. The start message names the file and the pipeline. See [Troubleshooting](quickstart.md#troubleshooting).

### Top-level keys

| Key | Required | Default | Meaning |
|---|---|---|---|
| `name` | Yes | None | The unique name of the pipeline. It matches `[a-z0-9][a-z0-9-]{0,63}`. |
| `version` | No | `1` | An integer of 1 or higher. Change it when the pipeline changes. |
| `summary` | No | Empty | A short text for callers. |
| `stream` | No | `allowed` | `allowed` lets the caller choose. `never` allows no stream. `required` allows only a stream. Asynchronous runs refuse such a pipeline. The value is not case-sensitive. |
| `input` | No | Any value | A JSON Schema, written as YAML. See [Input schema](#input-schema). |
| `steps` | Yes | None | A list of steps. The list must have at least one step that produces an answer. |

Any other top-level key stops the start, with a message that lists the known keys.

### Step keys

| Key | Required | Default | Meaning |
|---|---|---|---|
| `id` | No | The `uses` value with dots as hyphens, for example `rag-retrieve` | The unique ID of the step in the pipeline. It matches `[a-z0-9][a-z0-9_-]{0,63}`. A repeated default gets a number, for example `rag-retrieve-2`. |
| `uses` | Yes | None | The step type, for example `rag.retrieve`. |
| `with` | No | Empty object | The options of the step. A mapping. |

A minimal pipeline:

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

### Checks at start

The start checks each pipeline. These checks stop the start with a message:

- The name, the version and the step IDs have the right form, and the step IDs are unique.
- Each step type is registered.
- The options of each step match the options of the step type. An unknown option is an error.
- A `profile`, `model` or `template` option names a configured alias or an existing template file. An option that contains `${` is checked when the run starts.
- An expression path starts with `input.` or `steps.`. A `steps.` path names an earlier step.
- At least one step produces an answer.
- A pipeline with `stream: required` has a step that can stream.
- The input schema uses only supported keywords.

The message has this form, with the file, the pipeline and the step (real output with a bad step type):

```text
Pipeline 'bad': step 'not-a-step' uses the unknown step type 'not.a.step'.
```

## Built-in steps

The steps below are the built-in step types. Each option is optional unless the table says otherwise. The `steps.<id>.<name>` values are the results that later steps can read with an expression. A step without results stores nothing that an expression can read.

Each step has one class in `src/Xat.Orchestrator.Application/Steps/`. The option class has the same name with `Options`.

### prompt.system

Sets the system prompt of the run. The text can be empty. An empty text sets no system prompt.

| Option | Type | Meaning |
|---|---|---|
| `text` | text | The system prompt. |
| `template` | text | The reference `name@version` of a prompt template. |

Set exactly one of `text` and `template`. Results: none.

### prompt.user

Sets the prompt of the caller. The text is required. An empty text gives 400 `prompt.user_empty` when the run starts. Results: none.

| Option | Type | Meaning |
|---|---|---|
| `text` | text | The prompt. Usually `${input.prompt}`. |

### prompt.context

Adds context that the caller sent. The assemble step escapes the text and puts it in a `<context>` tag. An empty text adds nothing. Results: none.

| Option | Type | Default | Meaning |
|---|---|---|---|
| `text` | text | None | The context text. Usually `${input.context}`. |
| `label` | name | `caller-context` | The name of the section. Use it in the `order` option of `prompt.assemble`. |

### prompt.append

Adds an instruction that the pipeline author wrote. The text is trusted. The assemble step does not escape it, so never put caller text in it. Results: none.

| Option | Type | Default | Meaning |
|---|---|---|---|
| `text` | text | None | The instruction text. |
| `template` | text | None | The reference `name@version` of a prompt template. |
| `name` | name | `extra` | The name of the section. Use it in the `order` option of `prompt.assemble`. |

Set exactly one of `text` and `template`.

### rag.retrieve

Asks one xat-rag context for the chunks that match a question. A pipeline can use this step more than once.

| Option | Type | Default | Meaning |
|---|---|---|---|
| `profile` | alias | None, required | The retrieval alias. See `GET /v1/contexts`. |
| `chunks` | integer, 1 to 100 | The default of the alias | The highest number of chunks. |
| `query` | text | The prompt of the caller | The question. |

Results:

| Name | Type | Meaning |
|---|---|---|
| `steps.<id>.count` | integer | The number of chunks. |
| `steps.<id>.omitted` | integer | The number of candidates that xat-rag did not return. |
| `steps.<id>.chunks` | list | The chunks. Expressions cannot read this list. Use `flow.complete-if` or `retrieval.merge` instead. |

### retrieval.merge

Merges the chunks of earlier retrieval steps into one list. The step takes the chunks by rank, one from each source in turn. It does not compare scores. It removes chunks with the same ID when `dedupe` is `true`.

| Option | Type | Default | Meaning |
|---|---|---|---|
| `from` | list of step IDs, at least one | None, required | The earlier steps to merge. |
| `dedupe` | boolean | `true` | Removes chunks with the same ID. |
| `maxChunks` | integer, 1 to 1000 | No limit | The highest number of chunks in the result. |

Results: `steps.<id>.count` and `steps.<id>.chunks`. The step replaces the chunk sections of the sources in the prompt with one section.

### flow.complete-if

Ends the run early with a fixed answer when a condition is true. The run status is `no_answer`. The steps after this step do not run. Use this step for the path without sources.

| Option | Type | Meaning |
|---|---|---|
| `key` | path, required | The value to test. It starts with `input.` or with `steps.<id>.<name>`. |
| `isEmpty` | boolean | Set `true` to end the run when the value is empty. Set `false` to end it when the value is not empty. |
| `equals` | text | End the run when the value, as text, equals this text. |
| `text` | text, required | The answer that the run returns. |

Set exactly one of `isEmpty` and `equals`. Results: none.

### prompt.assemble

Builds the chat messages from all parts that the earlier steps added. The step does these tasks:

- It escapes text from documents and from the caller, and puts it in tags.
- It numbers the sources, so the model can cite them as `[n]`.
- It removes chunks from the end when the prompt does not fit the token budget. It adds a warning to the result.

| Option | Type | Default | Meaning |
|---|---|---|---|
| `model` | alias | `Llm:DefaultModel` | The model alias. The context window and the tokenizer come from this model. |
| `reserveOutput` | integer, 0 to 1000000 | `800` | The number of tokens that the step keeps free for the answer. |
| `order` | list of section names | The order in which the steps added the sections | The order of the sections in the prompt. A section that the list does not name goes after the named sections. |

Results: none. The messages are part of the run state, not of the `steps` values. Errors: `prompt.too_large`, `prompt.no_user`, `prompt.chunks_missing`.

### llm.generate

Sends the messages to xat-llm-engine and stores the answer. When the caller streams, the step sends each new part of the answer as an `answer.delta` event.

| Option | Type | Default | Meaning |
|---|---|---|---|
| `model` | alias | `Llm:DefaultModel` | The model alias. |
| `temperature` | number, 0 to 2 | The default of the engine | The sampling temperature. The adapter sends it only when the model lists `temperature` in `AllowedFields`. |
| `maxTokens` | integer, 1 to 1000000 | The default of the engine | The highest number of tokens in the answer. The adapter sends it only when the model lists `max_tokens` or `max_completion_tokens`. |

Results: none. The step adds the usage of the call to the run. It adds a warning when the model stopped at the token limit. Errors: `llm.no_messages`, `llm.model_unknown`, `llm.empty_answer`.

### answer.verify-citations

Checks the citations in the answer. The prompt numbers the sources, and the model cites the numbers, for example `[2]`. The step maps each number to its chunk. It adds a warning when a number has no source, and when the answer has no citation. It sends an `answer.citations` event when the prompt had sources.

This step has no options. Results: none. The citations appear in the response.

A link keeps its value when its scheme is `http` or `https`, or when it has no colon. Other links, for example `javascript:` links, become empty.

## Expressions

An expression is a text inside `${` and `}`. Use expressions in the `with` block of a step.

| Form | Meaning |
|---|---|
| `${input.prompt}` | The value of the input key `prompt`. |
| `${steps.retrieve.count}` | The value of a result of an earlier step. |
| `${input.context ?? 'default'}` | The value, or the default text when the value is missing. The quotes are optional. |

Expressions are lookups. They have no operators, functions or scripts. The only extra syntax is the `??` default.

These rules apply:

- A text that is only one expression keeps the type of the value. A number stays a number, and an object stays an object.
- A missing value without a default becomes `null`. Inside longer text, it becomes an empty text.
- An expression inside longer text becomes text. An object or a list becomes JSON text in the text.
- A list of chunks cannot be used in an expression. The run fails with `expression.invalid`.
- The start checks each path. A path starts with `input.` or `steps.`. A `steps.` path names an earlier step.

## Input schema

The `input` key of a pipeline is a JSON Schema, written in YAML. The orchestrator checks the input of each run against it. The orchestrator supports only a subset of JSON Schema. A pipeline that uses another keyword does not start.

Supported keywords:

- `type`, with `string`, `number`, `integer`, `boolean`, `object`, `array` or `null`. A list of types is allowed.
- `properties` and `required`
- `additionalProperties`, with the value `false` only
- `items`
- `enum`
- `minLength` and `maxLength` for text
- `minimum` and `maximum` for numbers
- `minItems` and `maxItems` for lists

The notes `title`, `description`, `default`, `examples`, `$schema` and `$id` are allowed. They have no effect on the check.

A message has the path of the value and the rule that failed. A message never repeats the value of the caller:

```text
input.language: the value is not one of the allowed values.
```

If the schema is missing, the pipeline accepts any input.

## Shipped pipelines

The repository has four pipelines in `pipelines/`. Each one is a YAML file. Read the file for the full definition. This section explains how each one works.

### rag-qa

The default pipeline. It answers a question from one retrieval alias and cites the sources.

Flow:

1. `system` sets the system prompt. The default text asks the model to answer from the sources. The caller can replace it with `system_prompt`.
2. `user` sets the prompt of the caller from `prompt`.
3. `retrieve` asks the alias from `context`, or `default`, for chunks.
4. `no-sources` ends the run with a fixed answer when there are no chunks.
5. `assemble` builds the messages within the token budget.
6. `generate` asks the model. The temperature is 0.2.
7. `citations` maps the `[n]` numbers to the sources.

Input: `prompt` (required), `context` (the alias, up to 64 characters) and `system_prompt`.

```bash
curl -s -X POST http://127.0.0.1:8430/v1/pipelines/rag-qa/runs \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Which port does the orders service use?", "context": "docs"}}'
```

### chat

Sends the prompt to the model with no retrieval. Use this pipeline for plain questions to the model.

Flow: `prompt.system` sets the system prompt from `system_prompt`, `prompt.user` sets the prompt, `prompt.assemble` builds the messages and `llm.generate` asks the model.

Input: `prompt` (required) and `system_prompt`.

```bash
curl -s -X POST http://127.0.0.1:8430/v1/pipelines/chat/runs \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Hi", "system_prompt": "Be brief."}}'
```

The text of `system_prompt` goes into the system message as it is. It is not escaped. Only programs on the same machine can send it.

### rag-sequential

Asks three retrieval aliases one after the other, then merges the chunks. The example uses the aliases `docs`, `jira` and `diffs`. Each alias points to a xat-rag context that holds one RAG.

Flow:

1. `system` sets a fixed system prompt.
2. `user` sets the prompt of the caller.
3. `docs`, `jira` and `diffs` run `rag.retrieve` in this order.
4. `merged` runs `retrieval.merge` with the three steps. It keeps at most 12 chunks and removes repeated chunks.
5. `no-sources` ends the run when the merged list is empty.
6. `assemble`, `generate` and `citations` run as in `rag-qa`.

Input: `prompt` (required). The caller cannot choose the aliases.

```bash
curl -s -X POST http://127.0.0.1:8430/v1/pipelines/rag-sequential/runs \
  -H "Content-Type: application/json" \
  -d '{"input": {"prompt": "Which port does the orders service use?"}}'
```

### custom-prompt

Builds the prompt from several parts. It follows the seven-step flow that the file describes. A final citation step checks the answer.

Flow:

1. `system` sets the system prompt from `system_prompt`.
2. `user` sets the prompt of the caller from `prompt`.
3. `caller-context` adds the text of `context`. The assemble step escapes it and puts it in a `<context>` tag.
4. `kb` retrieves chunks from the `default` alias.
5. `extra` adds the text of the template `extra-rules@1`. This text is trusted. It says that the answer has at most five sentences. It also asks for the language in `language`, when the caller sets it.
6. `assemble` builds the messages. The parts come in the order of the `order` option.
7. `generate` asks the model.
8. `citations` maps the `[n]` numbers to the sources.

Input: `prompt` (required), `system_prompt`, `context` (text from the caller) and `language`. The value of `language` is one of `English`, `German`, `French`, `Spanish` or `Greek`. The enum limits the value, because the template writes it into trusted text.

```bash
curl -s -X POST http://127.0.0.1:8430/v1/pipelines/custom-prompt/runs \
  -H "Content-Type: application/json" \
  -d '{"input": {"system_prompt": "You are a support agent.", "prompt": "Which port?", "context": "Customer uses v2.", "language": "German"}}'
```

Do not use free text from the caller in a trusted part, such as a template. Use an enum in the input schema instead.

## Settings

The settings come from `src/Xat.Orchestrator.Api/appsettings.json`. A setting can also come from a command-line option. An example is `--Api:MaxConcurrentRuns=4`. A setting can come from an environment variable too. Use `__` in place of `:`, for example `Api__MaxConcurrentRuns`. The `Urls` setting also accepts `--urls` and `ASPNETCORE_URLS`.

### Api

| Setting | Default | Meaning and rules |
|---|---|---|
| `Api:DefaultPipeline` | `rag-qa` | The pipeline for `POST /v1/ask`. The pipeline must exist. |
| `Api:MaxConcurrentRuns` | `8` | The highest number of runs at the same time. The value must be 1 or higher. |
| `Api:QueueLimit` | `16` | The number of runs that wait for a free run slot. The value must be 0 or higher. |
| `Api:MaxRequestBodyBytes` | `4194304` (4 MiB) | The largest request body. The value must be 1024 or higher. |

### Runs

| Setting | Default | Meaning and rules |
|---|---|---|
| `Runs:RunTimeout` | `00:03:00` | The time limit of one run. The value must be more than zero and at most 1 hour. |

### Jobs

| Setting | Default | Meaning and rules |
|---|---|---|
| `Jobs:Workers` | `2` | The number of asynchronous runs that execute at the same time. The value must be from 1 to 32. |
| `Jobs:WebhookWorkers` | `4` | The number of webhooks that the orchestrator sends at the same time. The value must be from 1 to 32. |
| `Jobs:QueueLimit` | `64` | The number of asynchronous runs that wait for a worker. A run above this number gets 503 `jobs.queue_full`. The value must be from 1 to 10000. |
| `Jobs:RetainFinished` | `500` | The highest number of finished runs that the store keeps. The value must be from 1 to 100000. |
| `Jobs:RetentionMinutes` | `60` | The number of minutes that the store keeps a finished run. The value must be from 1 to 10080. |

The store is in memory. These settings do not keep runs after a restart. See [Retention](#retention).

### Webhooks

| Setting | Default | Meaning and rules |
|---|---|---|
| `Webhooks:AllowedHosts` | Empty list | The host names where webhooks can go, in addition to loopback hosts. A host name ignores case. The value `*` allows every host. The list must not have an empty entry. |
| `Webhooks:TimeoutSeconds` | `10` | The time limit of one try, from 1 to 120. |
| `Webhooks:RetryCount` | `3` | The number of retries after a failed try, from 0 to 10. |
| `Webhooks:RetryDelayMilliseconds` | `1000` | The delay before the first retry, from 0 to 60000. The delay doubles with each retry, up to 30 seconds. |

A list setting can come from the file or from a command-line option. For example, `--Webhooks:AllowedHosts:0=hooks.example.com` adds one host. See [Allow another webhook host](quickstart.md#allow-another-webhook-host).

### Ingestion

| Setting | Default | Meaning and rules |
|---|---|---|
| `Ingestion:Enabled` | `true` | Turns the ingestion proxy on or off. When the value is `false`, every ingestion route gives 404 `ingestion.route_disabled`. |
| `Ingestion:DisabledRoutes` | Empty list | The route names to turn off. The names are in [Route names and paths](#route-names-and-paths). A name that does not exist stops the start. |
| `Ingestion:MaxUploadBytes` | `67108864` (64 MiB) | The largest upload in bytes, from 1024 to 1073741824. The limit applies only to `upload-documents`. xat-rag still refuses an upload above 64 MiB. |
| `Ingestion:ActivityTimeoutSeconds` | `120` | The time without data before the proxy ends the request, from 1 to 3600. |

The proxy sends its requests to the address in `Upstreams:XatRag`. A list setting can come from a command-line option. For example, `--Ingestion:DisabledRoutes:0=delete-document` turns off one route. See [Turn off a route](#turn-off-a-route).

### Upstreams

| Setting | Default | Meaning and rules |
|---|---|---|
| `Upstreams:XatRag:BaseUrl` | `http://127.0.0.1:8421` | The address of xat-rag. It must be an `http` loopback address. |
| `Upstreams:XatRag:ReadyFile` | Empty | The path of the ready file of xat-rag. The orchestrator reads it only when `BaseUrl` is empty. The file holds a JSON object with the key `url`. |
| `Upstreams:XatRag:RequestTimeoutSeconds` | `30` | The time limit of one call, with all retries, from 1 to 3600. The default in the class is 120. |
| `Upstreams:XatRag:RetryCount` | `2` | The number of retries, from 0 to 5. The default in the class is 2. |
| `Upstreams:XatLlmEngine:BaseUrl` | `http://127.0.0.1:8420` | The address of xat-llm-engine. It must be an `http` loopback address. |
| `Upstreams:XatLlmEngine:ReadyFile` | None | The same as for xat-rag. |
| `Upstreams:XatLlmEngine:RequestTimeoutSeconds` | `180` | The time limit of one call, with all retries, from 1 to 3600. The default in the class is 120. |
| `Upstreams:XatLlmEngine:RetryCount` | `2` | The number of retries, from 0 to 5. |

Each upstream is retried only after a 429, 503 or 504 answer, or after a failed connection. The retry waits for the `Retry-After` value, when the upstream sent one. Otherwise the wait starts at 500 milliseconds and grows.

When `BaseUrl` is set, the orchestrator uses it, and `ReadyFile` is not read. When the port of an upstream changes at each start, use `ReadyFile` and leave `BaseUrl` empty. The orchestrator reads the file again after a failed connection.

### Retrieval

| Setting | Default | Meaning and rules |
|---|---|---|
| `Retrieval:Profiles:<alias>:Context` | None, required | The name of the xat-rag context. It matches `[a-z0-9][a-z0-9-]{0,63}`. |
| `Retrieval:Profiles:<alias>:Chunks` | `8` in the class | The default number of chunks, from 1 to 100. |

The shipped aliases are `default` (context `ask-all`, 8 chunks), `docs` (`ask-docs`, 5), `jira` (`ask-jira`, 5) and `diffs` (`ask-diffs`, 5).

### Llm

| Setting | Default | Meaning and rules |
|---|---|---|
| `Llm:DefaultModel` | `default` | The model alias that steps use when they name no model. It must exist in `Llm:Models`. |
| `Llm:Models:<alias>:Model` | None, required | The model ID in the form `provider/model`. |
| `Llm:Models:<alias>:ContextWindow` | `8192` in the class | The context window in tokens. The value must be 256 or higher. |
| `Llm:Models:<alias>:Tokenizer` | `estimate` | The only supported value is `estimate`. See [Limits](#limits). |
| `Llm:Models:<alias>:Parallel` | `1` | The value must be 1 or higher. The orchestrator stores the value and does not use it to limit requests yet. |
| `Llm:Models:<alias>:SupportsGrammar` | `false` | The value shows in `GET /v1/models`. The orchestrator does not send grammars yet. |
| `Llm:Models:<alias>:AllowedFields` | Empty list | The top-level request fields that the adapter may send to xat-llm-engine. The adapter sends no other field. |

The shipped alias `default` uses the model `local/ministral-3-3b`. Its `AllowedFields` lists `temperature`, `max_tokens`, `stream_options`, `response_format` and `grammar`. The steps send only `temperature` and the token limit. A setting that is not in `AllowedFields` is not sent, and the adapter does not report it.

### Pipelines and Prompts

| Setting | Default | Meaning and rules |
|---|---|---|
| `Pipelines:Folder` | `pipelines` | The folder with the pipeline files. |
| `Prompts:Folder` | `prompts` | The folder with the prompt template files. A template file is named `name@version.liquid`. |

A relative folder starts at the folder of the orchestrator program. The build copies the files of `pipelines/` and `prompts/` into that folder. After you add a file, build the project again, or use an absolute folder path.

### Logging and hosts

| Setting | Default | Meaning |
|---|---|---|
| `Logging:LogLevel:Default` | `Information` | The default level of the log. |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Warning` | The level for the web framework. |
| `AllowedHosts` | `localhost;127.0.0.1;[::1]` | The host names that the orchestrator accepts in the `Host` header. This setting is not the list of webhook hosts. See `Webhooks:AllowedHosts`. |

## Limits

| Limit | Value | Where it is set |
|---|---|---|
| Request body | 4 MiB by default. A larger body gives 413. Uploads of the ingestion proxy use `Ingestion:MaxUploadBytes` instead. | `Api:MaxRequestBodyBytes` |
| Runs at the same time | 8 by default | `Api:MaxConcurrentRuns` |
| Runs that wait | 16 by default. A run that finds no free run slot and no free queue place gives 503 `admission.full`. | `Api:QueueLimit` |
| Time of one run | 3 minutes by default. The limit is at most 1 hour. A run above it gives 504 `run.timeout`. | `Runs:RunTimeout` |
| Time of one upstream call | 30 seconds for xat-rag and 180 seconds for xat-llm-engine by default. The time covers all retries. | `Upstreams:*:RequestTimeoutSeconds` |
| Retries of an upstream call | 2 by default, and at most 5. The ingestion proxy does not retry. | `Upstreams:*:RetryCount` |
| Question for retrieval | 20000 characters | Fixed in the code |
| Chunks for one retrieval | 1 to 100 | `rag.retrieve` option `chunks` |
| Chunks in a merge | 1 to 1000 | `retrieval.merge` option `maxChunks` |
| Temperature | 0 to 2 | `llm.generate` option `temperature` |
| Tokens in an answer | 1 to 1000000 | `llm.generate` option `maxTokens` |
| Events of one streamed run | 256 events wait in a buffer. When the buffer is full, the run waits for the caller. | Fixed in the code |
| Time to send the last event | 5 seconds. After this time the run stops sending the last event. | Fixed in the code |
| Time of a hook | 5 seconds for each hook call | Fixed in the code |
| Time of a health check of an upstream | 3 seconds | Fixed in the code |
| Length of an upstream message in an error | 300 characters | Fixed in the code |
| Token count | The orchestrator estimates 3 characters for each token. | Fixed in the code |
| Asynchronous runs at the same time | 2 by default | `Jobs:Workers` |
| Asynchronous runs that wait | 64 by default. A run above this number gets 503 `jobs.queue_full`. | `Jobs:QueueLimit` |
| Finished runs kept | 500 runs by default. A finished run stays for 60 minutes by default. | `Jobs:RetainFinished` and `Jobs:RetentionMinutes` |
| Webhooks that wait for delivery | 1000. A run that finds the delivery queue full ends its webhook as `failed`. | Fixed in the code |
| Webhooks sent at the same time | 4 by default | `Jobs:WebhookWorkers` |
| Time of one webhook try | 10 seconds by default | `Webhooks:TimeoutSeconds` |
| Retries of a webhook | 3 by default, and at most 10 | `Webhooks:RetryCount` |
| Upload size for the ingestion proxy | 64 MiB by default, and from 1024 bytes to 1 GiB. A declared size above the limit gives 413 `request.too_large`. A larger upload without a declared size gets the same error. | `Ingestion:MaxUploadBytes` |
| Inactivity of an ingestion request | 120 seconds by default, and from 1 to 3600 seconds. A request without data for this time gives 504 `xat-rag.timeout`. | `Ingestion:ActivityTimeoutSeconds` |
| Runs of the OpenAI-compatible route | The same limits as the run routes. | `Api:MaxConcurrentRuns` and `Api:QueueLimit` |
| Time of the list calls for `GET /v1/contexts` and `GET /v1/models` | 2 seconds for each call to xat-rag or xat-llm-engine. A slow call gives `available: null`. | Fixed in the code |

The token count is an estimate. The orchestrator has no tokenizer for the model. The count of xat-rag, `size_tokens`, is not a count of model tokens. The estimate is high on purpose, so the prompt stays inside the context window. The prompt budget also keeps `reserveOutput` tokens free for the answer.

The orchestrator has no idle timeout for a stream. A caller that stops reading keeps the connection open until the caller closes it.
