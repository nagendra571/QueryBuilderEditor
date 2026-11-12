using System;

namespace QueryBuilder.Launch
{
    /// <summary>The signed-in user in your app. The id is how the hub tells users apart, so it must be stable and never reused.</summary>
    public sealed class LaunchUser
    {
        public const int MaxIdLength = 256;
        public const int MaxNameLength = 200;
        public const int MaxEmailLength = 320;

        public LaunchUser(string id, string? name = null, string? email = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("The user id is required.", nameof(id));
            }

            if (id.Length > MaxIdLength)
            {
                throw new ArgumentException("The user id is longer than " + MaxIdLength + " characters.", nameof(id));
            }

            // The hub reserves this prefix for its own super admins, so it would refuse the token anyway.
            if (id.StartsWith("super:", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The user id may not start with 'super:'.", nameof(id));
            }

            if (name != null && name.Length > MaxNameLength)
            {
                throw new ArgumentException("The name is longer than " + MaxNameLength + " characters.", nameof(name));
            }

            if (email != null && email.Length > MaxEmailLength)
            {
                throw new ArgumentException("The email is longer than " + MaxEmailLength + " characters.", nameof(email));
            }

            Id = id;
            Name = string.IsNullOrEmpty(name) ? null : name;
            Email = string.IsNullOrEmpty(email) ? null : email;
        }

        public string Id { get; }

        public string? Name { get; }

        public string? Email { get; }
    }
}
