namespace QueryBuilder.Application.Abstractions;

public interface ICurrentUserService
{
    string UserId { get; }
    string DisplayName { get; }
    string? Email { get; }
}
