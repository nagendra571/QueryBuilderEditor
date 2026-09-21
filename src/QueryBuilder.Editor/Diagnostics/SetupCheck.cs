namespace QueryBuilder.Editor.Diagnostics;

internal sealed record SetupCheck(string Name, bool Passed, string Detail, string? Fix = null);
