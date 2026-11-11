using System.Text.Json;
using System.Text.Json.Serialization;
using Xat.Orchestrator.Domain.Generation;
using Xat.Orchestrator.Domain.Runs;

namespace Xat.Orchestrator.Api.Contracts;

/// <summary>The details of an error.</summary>
/// <param name="Type">The class of the error, for example <c>invalid_request_error</c>.</param>
/// <param name="Message">A short text for a person. It never repeats an input value.</param>
/// <param name="Code">A stable code for a program, for example <c>retrieval.timeout</c>.</param>
/// <param name="RunId">The ID of the run, or <see langword="null"/> when no run started.</param>
internal sealed record ErrorBody(string Type, string Message, string Code, string? RunId);
