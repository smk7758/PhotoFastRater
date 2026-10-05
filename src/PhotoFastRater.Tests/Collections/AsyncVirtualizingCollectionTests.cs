using FluentAssertions;
using PhotoFastRater.Core.Domain;
using PhotoFastRater.UI.Collections;
using Xunit;

namespace PhotoFastRater.Tests.Collections;

public sealed class AsyncVirtualizingCollectionTests
{
    [Fact]
    public async Task ForwardPagingRetainsAtMostFivePages()
    {
        const int total = 2_000;
        var collection = new AsyncVirtualizingCollection<int>((cursor, pageSize, _) =>
        {
            var start = cursor?.PhotoId ?? 0;
            var items = Enumerable.Range(start, Math.Min(pageSize, total - start)).ToArray();
            var nextValue = start + items.Length;
            PageCursor? next = nextValue < total ? new PageCursor(DateTime.UnixEpoch, nextValue) : null;
            return Task.FromResult(new PagedResult<int>(items, next, total));
        });

        await collection.ResetAsync();
        while (collection.HasMore)
            await collection.LoadNextAsync();

        collection.TotalCount.Should().Be(total);
        collection.Should().HaveCount(1_280);
        collection[0].Should().Be(720);
        collection[^1].Should().Be(1_999);
        collection.Dispose();
    }
}
