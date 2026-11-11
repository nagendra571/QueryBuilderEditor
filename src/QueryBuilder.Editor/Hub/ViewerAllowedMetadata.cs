namespace QueryBuilder.Editor.Hub;

/// <summary>Marks an endpoint a hub Viewer may call. Everything without it is closed to Viewers, so a new endpoint stays
/// closed until someone opts it in.</summary>
public sealed class ViewerAllowedMetadata
{
    public static readonly ViewerAllowedMetadata Instance = new();
}

public static class ViewerAllowedExtensions
{
    public static TBuilder AllowViewers<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(ViewerAllowedMetadata.Instance);
}
