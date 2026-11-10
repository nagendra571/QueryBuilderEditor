namespace QueryBuilder.Editor;

/// <summary>Hub mode: one QueryBuilder serving several apps from one metadata database. Off by default.</summary>
public sealed class HubOptions
{
    public bool Enabled { get; set; }

    /// <summary>The value every launch token's <c>aud</c> must equal (typically the hub's public URL). Required in hub mode.</summary>
    public string Audience { get; set; } = string.Empty;
}
