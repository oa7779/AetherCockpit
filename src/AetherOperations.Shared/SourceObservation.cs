namespace AetherOperations;

public enum SourceType { Direct, Derived, Database, Unconfirmed }
public enum DataAvailability { Available, Unavailable, AccessDenied, Unsupported, ProviderError, Stale, Unknown }
public enum DataConfidence { Unknown, Unconfirmed, ProviderReported, Corroborated }
public enum DataClassification { Normal, Sensitive, Secret }

public sealed record SourceMetadata
{
    public string Source { get; }
    public SourceType SourceType { get; }
    public DateTimeOffset Timestamp { get; }
    public DataConfidence Confidence { get; }
    public DataAvailability Availability { get; }
    public bool RequiresElevation { get; }
    public bool IsEstimated { get; }
    public bool IsCalculated => SourceType == SourceType.Derived;
    public DataClassification Classification { get; }
    public bool IsSensitive => Classification != DataClassification.Normal;
    public string? Error { get; }
    public Uri? DocumentationLink { get; }

    public SourceMetadata(string source, SourceType sourceType, DateTimeOffset timestamp,
        DataConfidence confidence, DataAvailability availability, bool requiresElevation = false,
        bool isEstimated = false, DataClassification classification = DataClassification.Normal,
        string? error = null, Uri? documentationLink = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!Enum.IsDefined(sourceType) || !Enum.IsDefined(confidence) ||
            !Enum.IsDefined(availability) || !Enum.IsDefined(classification))
            throw new ArgumentOutOfRangeException(nameof(sourceType), "Invalid source metadata classification.");
        if (documentationLink is not null && (!documentationLink.IsAbsoluteUri ||
            documentationLink.Scheme is not ("https" or "http") || documentationLink.UserInfo.Length != 0))
            throw new ArgumentException("Documentation must be an absolute HTTP(S) URL without credentials.", nameof(documentationLink));
        if (sourceType == SourceType.Unconfirmed && confidence is DataConfidence.ProviderReported or DataConfidence.Corroborated)
            throw new ArgumentException("Unconfirmed provenance cannot claim confirmed confidence.", nameof(confidence));
        Source = source;
        SourceType = sourceType;
        Timestamp = timestamp;
        Confidence = confidence;
        Availability = availability;
        RequiresElevation = requiresElevation;
        IsEstimated = isEstimated;
        Classification = classification;
        Error = error;
        DocumentationLink = documentationLink;
    }

    public SourceMetadata AsStale(string? error = null) =>
        new(Source, SourceType, Timestamp, Confidence, DataAvailability.Stale,
            RequiresElevation, IsEstimated, Classification, error, DocumentationLink);
}

public sealed class SourceObservation<T>
{
    public bool HasValue { get; }
    public T? Value { get; }
    public string Unit { get; }
    public SourceMetadata Metadata { get; }

    private SourceObservation(bool hasValue, T? value, string unit, SourceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(metadata);
        HasValue = hasValue;
        Value = value;
        Unit = unit;
        Metadata = metadata;
    }

    public static SourceObservation<T> Present(T value, string unit, SourceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.Availability is not (DataAvailability.Available or DataAvailability.Stale))
            throw new ArgumentException("A current value requires Available; retained older values require Stale.", nameof(metadata));
        if (value is double d && !double.IsFinite(d) || value is float f && !float.IsFinite(f))
            throw new ArgumentOutOfRangeException(nameof(value), "A measurement must be finite.");
        return new(true, value, unit, metadata);
    }

    public static SourceObservation<T> Missing(string unit, SourceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.Availability is DataAvailability.Available or DataAvailability.Stale)
            throw new ArgumentException("Missing data requires an explicit unavailable or unknown state.", nameof(metadata));
        return new(false, default, unit, metadata);
    }

    public SourceObservation<T> AsStale(string? error = null)
    {
        if (!HasValue) throw new InvalidOperationException("There is no retained value to mark stale.");
        return Present(Value!, Unit, Metadata.AsStale(error));
    }
}

public static class DiagnosticDataPolicy
{
    public static DataClassification Classify(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        return field.ToUpperInvariant() switch
        {
            "UNIT" or "SOURCETYPE" or "TIMESTAMP" or "AVAILABILITY" or "REQUIRESELEVATION" or
            "ISESTIMATED" or "ISCALCULATED" or "OPERATIONSTATE" or "EXITCODE" => DataClassification.Normal,
            "PASSWORD" or "PASSWORDHASH" or "TOKEN" or "DEVICETOKEN" or "ACCESSTOKEN" or
            "REFRESHTOKEN" or "CLAIMTOKEN" or "CSRFTOKEN" or "PROTECTEDTOKEN" or "CLIENTSECRET" or
            "RECOVERYKEY" or "RECOVERYPASSWORD" or "PRIVATEKEY" or "CERTIFICATEPRIVATEKEY" =>
                DataClassification.Secret,
            _ => DataClassification.Sensitive
        };
    }

    public static bool CanShare(DataClassification classification, bool sensitiveConsent) => classification switch
    {
        DataClassification.Normal => true,
        DataClassification.Sensitive => sensitiveConsent,
        DataClassification.Secret => false,
        _ => false
    };
}
