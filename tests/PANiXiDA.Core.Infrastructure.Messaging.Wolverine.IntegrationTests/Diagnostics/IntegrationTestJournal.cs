using System.Collections.Concurrent;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.IntegrationTests.Diagnostics;

public sealed class IntegrationTestJournal
{
    private readonly ConcurrentQueue<string> entries = new();

    public IReadOnlyList<string> Entries => [.. entries];

    public void Add(string entry)
    {
        entries.Enqueue(entry);
    }
}
