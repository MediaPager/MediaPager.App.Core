namespace MediaPager.App.Core.Models;

public sealed class RegistrationInvite
{
	public int Id { get; set; }
	public required string Email { get; set; }
	public required string TokenHash { get; set; }
	public required string CreatedByUserId { get; set; }
	public DateTimeOffset ExpiresAt { get; set; }
	public DateTimeOffset? UsedAt { get; set; }
}
