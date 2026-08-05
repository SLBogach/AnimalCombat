using System.Text.Json;
using Battle.Contracts.Versions;
using Battle.Replay.Json;
using Battle.Replay.Schema;

namespace Battle.Replay.Verification;

/// <summary>
/// Verifies a combat replay in the normative order: strict JSON, JSON Schema,
/// cross-field semantics, and the complete SHA-256 integrity chain.
/// </summary>
public sealed class ReplayVerifier
{
    private readonly JsonElement _schema;

    /// <summary>
    /// Creates a verifier backed by the externally versioned replay JSON Schema.
    /// </summary>
    /// <remarks>
    /// Schemas are intentionally not generated from C# types or embedded as an
    /// alternative source of truth. The caller supplies the machine-package bytes.
    /// </remarks>
    public ReplayVerifier(ReadOnlyMemory<byte> replaySchemaUtf8)
    {
        if (replaySchemaUtf8.IsEmpty)
        {
            throw new ArgumentException("Replay schema JSON is required.", nameof(replaySchemaUtf8));
        }

        try
        {
            using var document = JsonDocument.Parse(
                replaySchemaUtf8,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 256,
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Replay schema root must be an object.", nameof(replaySchemaUtf8));
            }

            _schema = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Replay schema is not valid JSON.", nameof(replaySchemaUtf8), exception);
        }
    }

    public ReplayVerificationResult Verify(ReadOnlyMemory<byte> replayUtf8Json)
    {
        var issues = new List<ReplayVerificationIssue>();
        if (!StrictReplayJsonReader.TryParse(replayUtf8Json, out var document, out var syntaxIssue))
        {
            issues.Add(
                new ReplayVerificationIssue(
                    ReplayVerificationLayer.Syntax,
                    ReplayVerificationSeverity.Error,
                    syntaxIssue!.Code,
                    syntaxIssue.Path,
                    syntaxIssue.Message));
            return new ReplayVerificationResult(issues, null, null, 0);
        }

        using (document)
        {
            var replay = document!.RootElement;
            var schemaIssues = ReplaySchemaValidator.Validate(replay, _schema);
            foreach (var schemaIssue in schemaIssues)
            {
                issues.Add(
                    new ReplayVerificationIssue(
                        ReplayVerificationLayer.Schema,
                        ReplayVerificationSeverity.Error,
                        ReplayVerificationCodes.SchemaViolation,
                        schemaIssue.Path,
                        schemaIssue.Message));
            }

            if (schemaIssues.Count > 0)
            {
                return new ReplayVerificationResult(issues, null, null, 0);
            }

            ReplaySemanticValidator.Validate(replay, issues);
            ReplayIntegrityValidator.Validate(
                replay,
                issues,
                out Sha256Digest computedInputDigest,
                out Sha256Digest computedFinalDigest);

            var eventCount = replay.GetProperty("events").GetArrayLength();
            return new ReplayVerificationResult(
                issues,
                computedInputDigest,
                computedFinalDigest,
                eventCount);
        }
    }
}
