using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Application.Abstractions;
using QueryBuilder.Application.DataSourceManagement;
using QueryBuilder.Application.Exceptions;
using QueryBuilder.Domain.Entities;
using QueryBuilder.Domain.Enums;
using QueryBuilder.Editor;
using QueryBuilder.Infrastructure.Persistence;
using QueryBuilder.Infrastructure.Repositories;
using QueryBuilder.Infrastructure.Security;
using static QueryBuilder.Tests.DataSourceManagementTests;

namespace QueryBuilder.Tests;

public class EncryptionKeyRotationTests
{
    private const byte Old = 7;
    private const byte New = 9;

    private static AesGcmConnectionSecretProtector Rotated(params byte[] previous) =>
        new(new ConnectionSecretKey(NewKey(New), previous.Select(p => NewKey(p)).ToList()));

    private static DataSource Managed(IConnectionSecretProtector protector, string name = "Warehouse", bool active = true)
    {
        var source = new DataSource { Name = name, Provider = DataSourceProvider.PostgreSql, ConnectionStringName = DataSource.ManagedConnectionSentinel, IsActive = active, CreatedBy = "x" };
        source.EncryptedConnection = protector.Protect(source.Id, ManagedConnections.Serialize(Details()));
        return source;
    }

    // ---- key, fingerprint, protector ----------------------------------------------------------------------

    [Fact]
    public void Fingerprint_is_eight_lowercase_hex_characters_and_stable()
    {
        var one = ConnectionSecretKey.Fingerprint(NewKey(Old));

        Assert.Matches("^[0-9a-f]{8}$", one);
        Assert.Equal(one, ConnectionSecretKey.Fingerprint(NewKey(Old)));
        Assert.NotEqual(one, ConnectionSecretKey.Fingerprint(NewKey(New)));
    }

    [Fact]
    public void Fingerprint_is_sha256_of_the_label_then_the_key()
    {
        var key = NewKey(Old);
        var input = System.Text.Encoding.ASCII.GetBytes("QueryBuilder connection key fingerprint v1").Concat(key).ToArray();
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input))[..8].ToLowerInvariant();

        Assert.Equal(expected, ConnectionSecretKey.Fingerprint(key));
    }

    [Fact]
    public void Unprotect_falls_back_to_a_previous_key_and_protect_uses_the_current_one()
    {
        var id = Guid.NewGuid();
        var oldBlob = Protector(Old).Protect(id, "hello");
        var rotated = Rotated(Old);

        Assert.Equal("hello", rotated.Unprotect(id, oldBlob));
        var fresh = rotated.Protect(id, "hello");
        Assert.Equal("hello", Protector(New).Unprotect(id, fresh));
        Assert.Throws<ConnectionSecretException>(() => Protector(Old).Unprotect(id, fresh));
    }

    [Fact]
    public void Unprotect_tries_previous_keys_in_order_and_a_key_that_is_listed_nowhere_fails()
    {
        var id = Guid.NewGuid();
        var rotated = Rotated(1, 2, Old);

        Assert.Equal("x", rotated.Unprotect(id, Protector(Old).Protect(id, "x")));
        Assert.Throws<ConnectionSecretException>(() => rotated.Unprotect(id, Protector(55).Protect(id, "x")));
    }

    [Fact]
    public void Identify_reports_current_previous_and_unreadable()
    {
        var id = Guid.NewGuid();
        var rotated = Rotated(Old);

        Assert.Equal(SecretKeyMatch.Current, rotated.Identify(id, Protector(New).Protect(id, "x")));
        Assert.Equal(SecretKeyMatch.Previous, rotated.Identify(id, Protector(Old).Protect(id, "x")));
        Assert.Equal(SecretKeyMatch.Unreadable, rotated.Identify(id, Protector(55).Protect(id, "x")));
        Assert.Equal(SecretKeyMatch.Unreadable, rotated.Identify(id, "not base64!!"));
        Assert.Equal(SecretKeyMatch.Unreadable, rotated.Identify(Guid.NewGuid(), Protector(New).Protect(id, "x")));
    }

    [Fact]
    public void Identify_without_a_key_is_unreadable_even_with_previous_keys_given()
    {
        var id = Guid.NewGuid();
        var none = new AesGcmConnectionSecretProtector(new ConnectionSecretKey(null, [NewKey(Old)]));

        Assert.Equal(SecretKeyMatch.Unreadable, none.Identify(id, Protector(Old).Protect(id, "x")));
        Assert.Null(none.CurrentFingerprint);
        Assert.Empty(none.PreviousFingerprints);
    }

    [Fact]
    public void The_protector_exposes_fingerprints_only()
    {
        var rotated = Rotated(Old);

        Assert.Equal(ConnectionSecretKey.Fingerprint(NewKey(New)), rotated.CurrentFingerprint);
        Assert.Equal([ConnectionSecretKey.Fingerprint(NewKey(Old))], rotated.PreviousFingerprints);
    }

    // ---- startup validation -------------------------------------------------------------------------------

    private static void Register(Action<QueryBuilderEditorOptions> configure) =>
        new ServiceCollection().AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=(localdb)\\none;Database=none";
            configure(o);
        });

    private static bool ReencryptAllowed(Action<QueryBuilderEditorOptions> configure)
    {
        var services = new ServiceCollection().AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=(localdb)\\none;Database=none";
            configure(o);
        });
        return services.Single(d => d.ServiceType == typeof(DataSourceManagementSettings)).ImplementationInstance
            is DataSourceManagementSettings { ReencryptAllowed: true };
    }

    [Fact]
    public void Reencrypt_follows_the_admin_rule_of_the_add_form_but_not_Enabled()
    {
        // No key: nothing to re-encrypt with.
        Assert.False(ReencryptAllowed(_ => { }));
        // Anonymous admin pages (the package default): refused, so anyone reaching the site can't start it early.
        Assert.False(ReencryptAllowed(o => o.DataSourceManagement.EncryptionKey = B64(New)));
        // Opted in for local development.
        Assert.True(ReencryptAllowed(o =>
        {
            o.DataSourceManagement.EncryptionKey = B64(New);
            o.DataSourceManagement.AllowAnonymousAdmins = true;
        }));
        // Restricted admins, with the add form off.
        Assert.True(ReencryptAllowed(o =>
        {
            o.DataSourceManagement.EncryptionKey = B64(New);
            o.AdminAuthorization.Mode = QueryBuilder.Editor.Authorization.QueryBuilderAuthorizationMode.Role;
            o.AdminAuthorization.RoleNames = ["Admin"];
        }));
        Assert.True(ReencryptAllowed(o =>
        {
            o.DataSourceManagement.EncryptionKey = B64(New);
            o.AdminAuthorization.PolicyName = "Admins";
        }));
    }

    private static string B64(byte fill) => Convert.ToBase64String(NewKey(fill));

    [Fact]
    public void Previous_keys_default_to_empty_and_valid_ones_are_accepted()
    {
        Assert.Empty(new QueryBuilderEditorOptions { ConnectionString = "x" }.DataSourceManagement.PreviousEncryptionKeys);

        Register(o =>
        {
            o.DataSourceManagement.EncryptionKey = B64(New);
            o.DataSourceManagement.PreviousEncryptionKeys = [B64(Old), B64(3)];
        });
    }

    [Theory]
    [InlineData("not base64!!")]
    [InlineData("AAAA")]
    [InlineData("   ")]
    public void A_bad_previous_key_names_its_index_and_never_echoes_the_value(string bad)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Register(o =>
        {
            o.DataSourceManagement.EncryptionKey = B64(New);
            o.DataSourceManagement.PreviousEncryptionKeys = [B64(Old), bad];
        }));

        Assert.Contains("PreviousEncryptionKeys[1]", ex.Message);
        if (bad.Trim().Length > 0)
        {
            Assert.DoesNotContain(bad.Trim(), ex.Message, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(B64(New), ex.Message);
        Assert.DoesNotContain(B64(Old), ex.Message);
    }

    [Fact]
    public void Previous_keys_without_a_current_key_are_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Register(o => o.DataSourceManagement.PreviousEncryptionKeys = [B64(Old)]));

        Assert.Contains("EncryptionKey", ex.Message);
        Assert.DoesNotContain(B64(Old), ex.Message);
    }

    [Fact]
    public void A_previous_key_equal_to_the_current_one_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Register(o =>
        {
            o.DataSourceManagement.EncryptionKey = B64(New);
            o.DataSourceManagement.PreviousEncryptionKeys = [B64(Old), " " + B64(New) + " "];
        }));

        Assert.Contains("PreviousEncryptionKeys[1]", ex.Message);
        Assert.DoesNotContain(B64(New), ex.Message);
    }

    [Fact]
    public void A_duplicate_previous_key_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Register(o =>
        {
            o.DataSourceManagement.EncryptionKey = B64(New);
            o.DataSourceManagement.PreviousEncryptionKeys = [B64(Old), B64(Old)];
        }));

        Assert.Contains("PreviousEncryptionKeys[1]", ex.Message);
        Assert.Contains("entry 0", ex.Message);
        Assert.DoesNotContain(B64(Old), ex.Message);
    }

    [Fact]
    public void Registered_services_read_a_source_written_under_a_previous_key()
    {
        var services = new ServiceCollection();
        services.AddQueryBuilderEditor(o =>
        {
            o.ConnectionString = "Server=(localdb)\\none;Database=none";
            o.DataSourceManagement.EncryptionKey = B64(New);
            o.DataSourceManagement.PreviousEncryptionKeys = [B64(Old)];
        });
        using var provider = services.BuildServiceProvider();
        var protector = provider.GetRequiredService<IConnectionSecretProtector>();
        var id = Guid.NewGuid();

        Assert.Equal("x", protector.Unprotect(id, Protector(Old).Protect(id, "x")));
        Assert.Single(protector.PreviousFingerprints);
    }

    // ---- status -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Status_counts_each_state_lists_only_sources_off_the_current_key_and_carries_no_secrets()
    {
        var rotated = Rotated(Old);
        var current = Managed(rotated, "Current");
        var previous = Managed(Protector(Old), "Previous");
        var previousOff = Managed(Protector(Old), "Previous off", active: false);
        var lost = Managed(Protector(55), "Lost");
        var config = new DataSource { Name = "Config", ConnectionStringName = "legacy" };
        var repo = new MemRepo(current, previous, previousOff, lost, config);

        var status = await new GetEncryptionKeyStatusQueryHandler(repo, rotated).Handle(new GetEncryptionKeyStatusQuery(), default);

        Assert.True(status.KeyConfigured);
        Assert.Equal(rotated.CurrentFingerprint, status.Fingerprint);
        Assert.Equal(rotated.PreviousFingerprints, status.PreviousFingerprints);
        Assert.Equal(4, status.ManagedCount);
        Assert.Equal(1, status.OnCurrentKey);
        Assert.Equal(2, status.OnPreviousKey);
        Assert.Equal(1, status.Unreadable);
        Assert.Equal(["Lost", "Previous", "Previous off"], status.Sources.Select(s => s.Name).OrderBy(n => n));
        Assert.Equal(SecretKeyMatch.Unreadable, status.Sources.Single(s => s.Name == "Lost").State);
        Assert.False(status.Sources.Single(s => s.Name == "Previous off").IsActive);

        var json = JsonSerializer.Serialize(status, CamelJson);
        foreach (var source in new[] { current, previous, previousOff, lost })
        {
            Assert.DoesNotContain(source.EncryptedConnection!, json);
        }

        Assert.DoesNotContain(Secret, json);
        Assert.DoesNotContain("db.example.com", json);
        Assert.DoesNotContain(B64(New), json);
        Assert.DoesNotContain(B64(Old), json);
    }

    [Fact]
    public async Task Status_without_a_key_reports_every_managed_source_unreadable()
    {
        var none = new AesGcmConnectionSecretProtector(new ConnectionSecretKey(null));
        var repo = new MemRepo(Managed(Protector(Old)));

        var status = await new GetEncryptionKeyStatusQueryHandler(repo, none).Handle(new GetEncryptionKeyStatusQuery(), default);

        Assert.False(status.KeyConfigured);
        Assert.Null(status.Fingerprint);
        Assert.Equal(1, status.Unreadable);
        Assert.Single(status.Sources);
    }

    // ---- re-encrypt ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Reencrypt_moves_previous_to_current_and_leaves_the_others_alone()
    {
        var rotated = Rotated(Old);
        var current = Managed(rotated, "Current");
        var previous = Managed(Protector(Old), "Previous");
        var lost = Managed(Protector(55), "Lost");
        var currentBlob = current.EncryptedConnection;
        var lostBlob = lost.EncryptedConnection;
        var repo = new MemRepo(current, previous, lost);
        var audit = new AuditCapture();

        var result = await new ReencryptDataSourcesCommandHandler(repo, rotated, audit).Handle(new ReencryptDataSourcesCommand(), default);

        Assert.Equal(new ReencryptResultDto(1, 1, 1, 0), result);
        Assert.Equal(SecretKeyMatch.Current, rotated.Identify(previous.Id, previous.EncryptedConnection!));
        Assert.Equal(Details(), ManagedConnections.TryRead(previous, Protector(New)));
        Assert.Equal(currentBlob, current.EncryptedConnection);
        Assert.Equal(lostBlob, lost.EncryptedConnection);
    }

    [Fact]
    public async Task Reencrypt_is_idempotent()
    {
        var rotated = Rotated(Old);
        var previous = Managed(Protector(Old));
        var repo = new MemRepo(previous);
        var handler = new ReencryptDataSourcesCommandHandler(repo, rotated, new AuditCapture());

        await handler.Handle(new ReencryptDataSourcesCommand(), default);
        var blob = previous.EncryptedConnection;
        var second = await handler.Handle(new ReencryptDataSourcesCommand(), default);

        Assert.Equal(new ReencryptResultDto(0, 1, 0, 0), second);
        Assert.Equal(blob, previous.EncryptedConnection);
    }

    [Fact]
    public async Task Reencrypt_covers_turned_off_sources()
    {
        var rotated = Rotated(Old);
        var off = Managed(Protector(Old), active: false);

        var result = await new ReencryptDataSourcesCommandHandler(new MemRepo(off), rotated, new AuditCapture())
            .Handle(new ReencryptDataSourcesCommand(), default);

        Assert.Equal(1, result.Reencrypted);
        Assert.False(off.IsActive);
    }

    [Fact]
    public async Task Reencrypt_without_a_key_is_a_validation_error()
    {
        var none = new AesGcmConnectionSecretProtector(new ConnectionSecretKey(null));
        var audit = new AuditCapture();

        var ex = await Assert.ThrowsAsync<CatalogValidationException>(() =>
            new ReencryptDataSourcesCommandHandler(new MemRepo(Managed(Protector(Old))), none, audit).Handle(new ReencryptDataSourcesCommand(), default));

        Assert.Contains("EncryptionKey", ex.Message);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Reencrypt_writes_one_audit_entry_with_counts_and_fingerprints_and_no_secrets()
    {
        var rotated = Rotated(Old);
        var previous = Managed(Protector(Old));
        var lost = Managed(Protector(55));
        var audit = new AuditCapture();

        await new ReencryptDataSourcesCommandHandler(new MemRepo(previous, lost), rotated, audit).Handle(new ReencryptDataSourcesCommand(), default);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditAction.EncryptionKeyRotated, entry.Action);
        var text = entry.Summary + JsonSerializer.Serialize(entry.Details, CamelJson);
        Assert.Contains(rotated.CurrentFingerprint!, text);
        Assert.Contains(rotated.PreviousFingerprints[0], text);
        Assert.Contains("\"reencrypted\":1", text);
        Assert.Contains("\"unreadable\":1", text);
        Assert.DoesNotContain(Secret, text);
        Assert.DoesNotContain("db.example.com", text);
        Assert.DoesNotContain(B64(New), text);
        Assert.DoesNotContain(B64(Old), text);
        Assert.DoesNotContain(previous.EncryptedConnection!, text);
        Assert.DoesNotContain(lost.EncryptedConnection!, text);
    }

    [Fact]
    public async Task Reencrypt_skips_a_source_edited_while_it_ran_instead_of_restoring_the_old_details()
    {
        var rotated = Rotated(Old);
        var previous = Managed(Protector(Old));
        var inner = new MemRepo(previous);
        var edited = rotated.Protect(previous.Id, ManagedConnections.Serialize(Details() with { Password = "typed-meanwhile" }));
        var racing = new RacingRepo(inner, () => previous.EncryptedConnection = edited);

        var result = await new ReencryptDataSourcesCommandHandler(racing, rotated, new AuditCapture())
            .Handle(new ReencryptDataSourcesCommand(), default);

        Assert.Equal(new ReencryptResultDto(0, 0, 0, 1), result);
        Assert.Equal(edited, previous.EncryptedConnection);
    }

    [MetadataSqlFact]
    public async Task The_conditional_write_only_replaces_a_blob_that_still_equals_the_one_read()
    {
        var server = Environment.GetEnvironmentVariable(MetadataSqlFactAttribute.EnvironmentVariable)!;
        var name = $"qb_meta_it_{Guid.NewGuid():N}";
        var connectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)).Options;

        try
        {
            await using (var setup = new AppDbContext(options))
            {
                await setup.Database.MigrateAsync();
            }

            var rotated = Rotated(Old);
            var source = Managed(Protector(Old));
            var original = source.EncryptedConnection!;
            await using (var seed = new AppDbContext(options))
            {
                await new DataSourceRepository(seed).AddAsync(source, default);
                await seed.SaveChangesAsync();
            }

            var edited = rotated.Protect(source.Id, ManagedConnections.Serialize(Details() with { Password = "typed-meanwhile" }));
            var stale = rotated.Protect(source.Id, ManagedConnections.Serialize(Details()));

            await using (var db = new AppDbContext(options))
            {
                var repo = new DataSourceRepository(db);
                Assert.True(await repo.TryReplaceEncryptedConnectionAsync(source.Id, original, edited, default));
                Assert.False(await repo.TryReplaceEncryptedConnectionAsync(source.Id, original, stale, default));
                Assert.False(await repo.TryReplaceEncryptedConnectionAsync(Guid.NewGuid(), original, stale, default));
            }

            await using (var check = new AppDbContext(options))
            {
                var row = await check.DataSources.AsNoTracking().SingleAsync(d => d.Id == source.Id);
                Assert.Equal(edited, row.EncryptedConnection);
                Assert.Equal(source.Name, row.Name);
                Assert.True(row.IsActive);
            }
        }
        finally
        {
            SqlConnection.ClearAllPools();
            var master = new SqlConnectionStringBuilder(server) { InitialCatalog = "master" }.ConnectionString;
            await using var connection = new SqlConnection(master);
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public void Audit_action_is_appended_after_the_existing_values()
    {
        Assert.Equal(23, (int)AuditAction.DataSourceDeactivated);
        Assert.Equal(24, (int)AuditAction.EncryptionKeyRotated);
    }

    /// <summary>Lets a test change the stored blob just before the conditional write, as another admin's save would.</summary>
    private sealed class RacingRepo(MemRepo inner, Action beforeReplace) : IDataSourceRepository
    {
        public Task<DataSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => inner.GetByIdAsync(id, cancellationToken);
        public Task<List<DataSource>> GetActiveAsync(CancellationToken cancellationToken) => inner.GetActiveAsync(cancellationToken);
        public Task<List<DataSource>> GetAllAsync(CancellationToken cancellationToken) => inner.GetAllAsync(cancellationToken);
        public Task AddAsync(DataSource dataSource, CancellationToken cancellationToken) => inner.AddAsync(dataSource, cancellationToken);
        public void UpdateOnly(DataSource dataSource, params string[] propertyNames) => inner.UpdateOnly(dataSource, propertyNames);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => inner.SaveChangesAsync(cancellationToken);

        public Task<bool> TryReplaceEncryptedConnectionAsync(Guid id, string expected, string replacement, CancellationToken cancellationToken)
        {
            beforeReplace();
            return inner.TryReplaceEncryptedConnectionAsync(id, expected, replacement, cancellationToken);
        }
    }
}
