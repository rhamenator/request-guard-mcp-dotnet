using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RequestGuardMcp.Core.Models.Request;

/// <summary>Primary classification request. Ports src/models/request.rs's <c>ClassifyRequest</c>.</summary>
public sealed class ClassifyRequest
{
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public string? Path { get; set; }
    public string? Method { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public string? BodySnippet { get; set; }
    public string? Referer { get; set; }
    public string? Accept { get; set; }
    public string? RequestId { get; set; }
    public string? Timestamp { get; set; }

    /// <summary>TLS metadata is never trusted without a valid short-lived attestation.</summary>
    public string? TlsJa3 { get; set; }

    public string? TlsJa4 { get; set; }
    public string? TlsFingerprintSource { get; set; }
    public string? TlsFingerprintAttestation { get; set; }

    /// <summary>Server-derived provenance. Excluded from JSON entirely: a client cannot set this.</summary>
    [JsonIgnore]
    public bool TlsFingerprintVerified { get; set; }

    public JsonNode? Extra { get; set; }
}
