using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Infrastructure.Database;
using PhotoFastRater.Infrastructure.Database.Repositories;
using PhotoFastRater.Infrastructure.Metadata;
using Xunit;

namespace PhotoFastRater.Tests.Metadata;

/// <summary>Verifies that synchronous window shutdown never requires the UI dispatcher to run.</summary>
public sealed class XmpSyncQueueTests
{
    [Fact]
    public async Task DisposeOnUiContextCompletesWithoutDispatchingContinuations()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new NonPumpingSynchronizationContext();
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var factory = Mock.Of<IDbContextFactory<PhotoDbContext>>();
                using (var queue = new XmpSyncQueue(
                    new PhotoRepository(factory),
                    Mock.Of<IXmpSidecarStore>(),
                    NullLogger<XmpSyncQueue>.Instance))
                {
                    // An idle channel still has an asynchronous wait that must cancel during exit.
                }
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        context.PostCount.Should().Be(0);
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;
        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback callback, object? state) =>
            Interlocked.Increment(ref _postCount);
    }
}
