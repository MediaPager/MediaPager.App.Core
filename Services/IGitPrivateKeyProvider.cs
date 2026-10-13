namespace MediaPager.App.Core.Services;

/// <summary>Provides short-lived files for Git SSH operations. Implementations must not
/// persist the private key in plaintext beyond the lifetime of the returned lease.</summary>
public interface IGitPrivateKeyProvider
{
    Task<IGitPrivateKeyLease?> CreateLeaseAsync(CancellationToken cancellationToken = default);
}

public interface IGitPrivateKeyLease : IAsyncDisposable
{
    string Path { get; }
}
