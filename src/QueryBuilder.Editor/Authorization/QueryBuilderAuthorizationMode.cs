namespace QueryBuilder.Editor.Authorization;

public enum QueryBuilderAuthorizationMode
{
    /// <summary>Open to all users — no authentication required. Default, fully backward compatible.</summary>
    Anonymous = 0,

    /// <summary>Any signed-in user can access the editor.</summary>
    Authenticated = 1,

    /// <summary>A user in any of <see cref="QueryBuilderAuthorizationOptions.RoleNames"/> is granted access (OR logic).</summary>
    Role = 2
}
