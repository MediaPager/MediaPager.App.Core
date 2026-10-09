namespace MediaPager.App.Core.Services;

/// <summary>
/// Repo-URL forms accepted by the plugin pipeline and the git transport they map to.
/// Public repos are plain HTTPS with zero config — no keys, no ~/.ssh/config, works for
/// any user (www-data on a server included). ssh-form URLs (git@host:path or ssh://…)
/// are rewritten to https on the fly so pasting GitHub's ssh clone URL still works;
/// when <c>MEDIAPAGER_GIT_SSH_PRIVATE_KEY_PATH</c> points at a private key, ssh is kept
/// and git is given -c core.sshCommand=… with that key — the private-repo path, still
/// without any machine-level ssh config.
/// </summary>
public static class GitRepoUrl
{
    public const string SshKeyPathVariable = "MEDIAPAGER_GIT_SSH_PRIVATE_KEY_PATH";

    /// <summary>ssh://, git+ssh://, or scp-like git@host:path (a colon after the '@',
    /// and no '/' in the host part — so "owner/repo" or "foo:bar@baz" don't match).</summary>
    public static bool IsSshLike(string repo)
    {
        var trimmed = repo.Trim();
        if (trimmed.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("git+ssh://", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed.Contains("://", StringComparison.Ordinal))
            return false;
        var atIndex = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0)
            return false;
        var colonIndex = trimmed.IndexOf(':', atIndex + 1);
        return colonIndex > atIndex && !trimmed[(atIndex + 1)..colonIndex].Contains('/');
    }

    /// <summary>ssh-form → https URL; anything else passes through.</summary>
    public static string ToHttps(string repo)
    {
        var trimmed = repo.Trim();
        if (trimmed.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("git+ssh://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
                return trimmed;
            var port = uri.Port is > 0 and not 22 ? $":{uri.Port}" : string.Empty;
            return $"https://{uri.Host}{port}{uri.AbsolutePath}".TrimEnd('/');
        }
        if (IsSshLike(trimmed))
        {
            var atIndex = trimmed.IndexOf('@', StringComparison.Ordinal);
            var colonIndex = trimmed.IndexOf(':', atIndex + 1);
            var host = trimmed[(atIndex + 1)..colonIndex];
            var path = trimmed[(colonIndex + 1)..].TrimStart('/');
            return $"https://{host}/{path}".TrimEnd('/');
        }
        return trimmed;
    }

    /// <summary>Identity form for comparing repo URLs across transports:
    /// host/path, no scheme, user, .git suffix, or trailing slash.</summary>
    public static string Normalize(string repo)
    {
        var trimmed = repo.Trim().TrimEnd('/');
        if (IsSshLike(trimmed))
            trimmed = ToHttps(trimmed);
        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["https://".Length..];
        else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["http://".Length..];
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^".git".Length];
        return trimmed.TrimEnd('/');
    }

    /// <summary>The URL to hand git plus any -c arguments it needs. ssh-form URLs stay
    /// ssh only when <c>MEDIAPAGER_GIT_SSH_PRIVATE_KEY_PATH</c> is set (and exists);
    /// otherwise they become https. Returns an empty argument prefix for plain https.</summary>
    public static (string Url, string GitArguments) Resolve(string repo)
    {
        var trimmed = repo.Trim();
        if (!IsSshLike(trimmed))
            return (trimmed, string.Empty);

        var keyPath = Environment.GetEnvironmentVariable(SshKeyPathVariable);
        if (string.IsNullOrWhiteSpace(keyPath))
            return (ToHttps(trimmed), string.Empty);
        if (!File.Exists(keyPath))
            throw new InvalidOperationException(
                $"{SshKeyPathVariable} points at '{keyPath}', which does not exist.");

        // git runs this through sh -c, so single quotes around the path survive spaces;
        // BatchMode forbids any interactive prompt (a server must never hang on git).
        var sshCommand =
            $"ssh -i '{keyPath}' -o IdentitiesOnly=yes -o BatchMode=yes -o StrictHostKeyChecking=accept-new";
        return (trimmed, $"-c \"core.sshCommand={sshCommand}\" ");
    }
}
