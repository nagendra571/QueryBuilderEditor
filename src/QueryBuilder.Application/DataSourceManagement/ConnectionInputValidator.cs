using FluentValidation;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Domain.Enums;

namespace QueryBuilder.Application.DataSourceManagement;

public sealed class ConnectionInputValidator : AbstractValidator<ConnectionInput>
{
    private static readonly char[] ForbiddenHostCharacters = [',', ';', '=', '"', '\''];

    private static bool NoControlCharacters(string? value) => value is null || value.All(c => !char.IsControl(c));

    public ConnectionInputValidator()
    {
        RuleFor(x => x.Host).NotEmpty().MaximumLength(255)
            .Must(host => host is null || host.All(c => !char.IsControl(c) && !char.IsWhiteSpace(c)))
            .WithMessage("The host can't contain spaces or control characters.")
            // Postgres reads a comma as a second host; the rest is connection-string syntax.
            .Must(host => host is null || host.IndexOfAny(ForbiddenHostCharacters) < 0)
            .WithMessage("The host can't contain commas, semicolons, equals signs or quotes.")
            .Must(host => host is null || host.Length == 0 || ManagedConnections.IsAcceptableHost(host, allowInstance: true))
            .WithMessage(ManagedConnections.HostMessage);
        RuleFor(x => x.Port).InclusiveBetween(1, 65535).When(x => x.Port is not null);
        RuleFor(x => x.Database).NotEmpty().MaximumLength(128).Must(NoControlCharacters)
            .WithMessage("The database name can't contain control characters.");
        RuleFor(x => x.Username).NotEmpty().MaximumLength(128).Must(NoControlCharacters)
            .WithMessage("The username can't contain control characters.");
        RuleFor(x => x.Password).MaximumLength(1024).Must(NoControlCharacters)
            .WithMessage("The password can't contain control characters.");
        RuleFor(x => x.SslMode).Must(mode => mode is null || Enum.IsDefined(mode.Value))
            .WithMessage("Choose a supported encryption setting.");
    }
}

/// <summary>Name and provider rules shared by create and edit.</summary>
public static class DataSourceFieldRules
{
    /// <summary>Required, up to 200 characters, and unique ignoring case among data sources other than the row itself.</summary>
    public static IRuleBuilderOptions<T, string?> UniqueName<T>(
        this IRuleBuilder<T, string?> rule, IDataSourceRepository repository, Func<T, Guid?> selfId) =>
        rule.NotEmpty().MaximumLength(200)
            .MustAsync(async (command, name, ct) =>
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return true;
                }

                var self = selfId(command);
                var all = await repository.GetAllAsync(ct);
                return !all.Any(d => d.Id != self && string.Equals(d.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            })
            .WithMessage((_, name) => $"A data source named '{name?.Trim()}' already exists. Choose a different name.");

    public static IRuleBuilderOptions<T, DataSourceProvider> SupportedProvider<T>(this IRuleBuilder<T, DataSourceProvider> rule) =>
        rule.Must(ManagedConnections.IsSupportedProvider)
            .WithMessage("Only SQL Server and PostgreSQL data sources can be added here.");
}
