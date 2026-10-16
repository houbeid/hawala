using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Time;

namespace Hawala.SharedKernel.Tests.Ids;

public class IdGeneratorTests
{
    [Fact]
    public void New_id_is_a_version_7_uuid()
    {
        var id = IdGenerator.NewId();

        Assert.Equal(7, id.Version);
    }

    [Fact]
    public void Ids_are_unique()
    {
        var ids = Enumerable.Range(0, 10_000).Select(_ => IdGenerator.NewId()).ToHashSet();

        Assert.Equal(10_000, ids.Count);
    }

    [Fact]
    public void Ids_are_ordered_by_creation_time()
    {
        var earlier = IdGenerator.NewId(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));
        var later = IdGenerator.NewId(new DateTimeOffset(2026, 10, 13, 9, 0, 1, TimeSpan.Zero));

        // Ordre du texte = ordre de tri dans PostgreSQL (uuid comparé octet par octet).
        Assert.True(string.CompareOrdinal(earlier.ToString(), later.ToString()) < 0);
    }

    [Fact]
    public void System_clock_returns_utc()
    {
        var now = new SystemClock().UtcNow;

        Assert.Equal(TimeSpan.Zero, now.Offset);
    }
}