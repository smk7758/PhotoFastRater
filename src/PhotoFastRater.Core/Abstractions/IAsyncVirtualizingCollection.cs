using System.Collections.Specialized;
using System.ComponentModel;

namespace PhotoFastRater.Core.Abstractions;

/// <summary>Represents a bounded UI window over a much larger asynchronously paged result set.</summary>
public interface IAsyncVirtualizingCollection<out T> : IReadOnlyList<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    /// <summary>Gets the SQL-reported result count without materializing those rows.</summary>
    long TotalCount { get; }

    /// <summary>Gets whether another forward cursor is available.</summary>
    bool HasMore { get; }

    /// <summary>Replaces the window with the first page for a new query.</summary>
    Task ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>Appends the next page and evicts the oldest page after the configured window limit.</summary>
    Task LoadNextAsync(CancellationToken cancellationToken = default);
}
