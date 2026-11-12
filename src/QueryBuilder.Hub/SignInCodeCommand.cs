using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using QueryBuilder.Editor.Hub;

namespace QueryBuilder.Hub;

/// <summary>
/// <c>signin-code --name &lt;name&gt; [--minutes &lt;1-60&gt;]</c>: issues a one-time super admin sign-in code from the server.
/// It builds the host's services from the same configuration but never starts them, so nothing migrates or serves; the
/// code is printed only after its audit record is saved.
/// </summary>
public static class SignInCodeCommand
{
    public const string Name = "signin-code";

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            string? name = null;
            int? minutes = null;
            var rest = new List<string>();
            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--name" when i + 1 < args.Length:
                        name = args[++i];
                        break;
                    case "--minutes" when i + 1 < args.Length:
                        minutes = int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
                            ? m
                            : throw new ArgumentException("--minutes must be a whole number.");
                        break;
                    default:
                        rest.Add(args[i]); // configuration overrides such as --Hub:ConnectionString=...
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Usage: signin-code --name <name> [--minutes <1-60>]");
            }

            await using var app = HubHost.Build([.. rest], forCommand: true);
            var issued = await app.Services.GetRequiredService<ISuperAdminCodeIssuer>().IssueAsync(name, minutes, CancellationToken.None);
            await stdout.WriteLineAsync($"Code: {issued.Code}");
            await stdout.WriteLineAsync($"Expires: {issued.ExpiresUtc.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)}");
            return 0;
        }
        catch (Exception ex)
        {
            // Never the code: an exception here happened before it was returned, and its message holds no secret.
            await stderr.WriteLineAsync($"Could not issue a sign-in code: {ex.Message}");
            return 1;
        }
    }
}
