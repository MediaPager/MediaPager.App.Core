using Microsoft.AspNetCore.Identity;

namespace MediaPager.App.Core.Models;

public sealed class AppUser : IdentityUser
{
	public string Scopes { get; set; } = "";
	public bool IsActive { get; set; } = true;
	public DateTimeOffset? DeletedAt { get; set; }

	public string? FirstName { get; set; }
	public string? LastName { get; set; }

	// Pending verified email change; null when none is in flight.
	public string? PendingEmail { get; set; }
	public string? PendingEmailCodeHash { get; set; }
	public DateTimeOffset? PendingEmailExpiresAt { get; set; }

	public IReadOnlyCollection<string> GetScopes() => Scopes
		.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
		.Select(scope => string.Equals(scope, "admin:can-invite", StringComparison.OrdinalIgnoreCase)
			? "users:can-invite"
			: scope.ToLowerInvariant())
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.ToArray();
}
