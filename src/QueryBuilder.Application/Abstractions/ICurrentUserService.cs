namespace QueryBuilder.Application.Abstractions;

public interface ICurrentUserService
{
    string UserId { get; }
    string DisplayName { get; }
    string? Email { get; }

    /// <summary>The hub session's role; null when embedded or without a session.</summary>
    QueryBuilder.Domain.Enums.HubRole? Role { get; }

    bool IsSuperAdmin { get; }
}
