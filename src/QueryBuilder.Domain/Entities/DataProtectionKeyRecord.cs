namespace QueryBuilder.Domain.Entities;

/// <summary>One ASP.NET Core Data Protection key ring entry, stored so every instance shares session cookies.</summary>
public sealed class DataProtectionKeyRecord : IHubSystemRecord
{
    public int Id { get; set; }
    public string FriendlyName { get; set; } = string.Empty;
    public string Xml { get; set; } = string.Empty;
}
