namespace JitHub.GitHub.Abstractions.Models;

public readonly record struct RepoKey
{
    public RepoKey(string owner, string name)
    {
        if (owner is null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        owner = owner.Trim();
        name = name.Trim();

        if (owner.Length == 0)
        {
            throw new ArgumentException("Owner must not be empty.", nameof(owner));
        }

        if (name.Length == 0)
        {
            throw new ArgumentException("Name must not be empty.", nameof(name));
        }

        Owner = owner;
        Name = name;
    }

    public string Owner { get; }

    public string Name { get; }

    public override string ToString() => $"{Owner}/{Name}";
}
