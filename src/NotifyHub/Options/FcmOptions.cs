namespace NotifyHub.Options;

/// <summary>
/// Credentials for Firebase Cloud Messaging (Android, HTTP v1 API). The Google service account
/// is passed as JSON content (not as a path) - this lets the host app load it from a file, a
/// secret store, or an environment variable without this library dictating a specific source.
/// </summary>
public sealed record FcmOptions
{
    /// <summary>Raw content of the service account JSON file from the Firebase console.</summary>
    public required string ServiceAccountJson { get; init; }
    /// <summary>Firebase project ID (part of the target URL).</summary>
    public required string ProjectId { get; init; }

    public static FcmOptions FromFile(string path, string projectId) => new()
    {
        ServiceAccountJson = File.ReadAllText(path),
        ProjectId = projectId,
    };
}
