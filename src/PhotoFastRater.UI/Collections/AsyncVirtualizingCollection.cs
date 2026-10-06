using System.Collections.ObjectModel;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Domain;

namespace PhotoFastRater.UI.Collections;

/// <summary>Keeps at most five 256-item pages while retaining a stable keyset continuation.</summary>
public sealed class AsyncVirtualizingCollection<T> : ObservableCollection<T>, IAsyncVirtualizingCollection<T>, IDisposable
{
    private const int PageSize = 256;
    private const int MaximumItems = PageSize * 5;
    private readonly Func<PageCursor?, int, CancellationToken, Task<PagedResult<T>>> _pageLoader;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private PageCursor? _nextCursor;
    private bool _hasLoaded;
    private long _totalCount;

    /// <summary>Creates a collection around a cursor page loader.</summary>
    public AsyncVirtualizingCollection(Func<PageCursor?, int, CancellationToken, Task<PagedResult<T>>> pageLoader) =>
        _pageLoader = pageLoader ?? throw new ArgumentNullException(nameof(pageLoader));

    /// <inheritdoc />
    public long TotalCount
    {
        get => _totalCount;
        private set
        {
            if (_totalCount == value)
                return;
            _totalCount = value;
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(TotalCount)));
        }
    }

    /// <inheritdoc />
    public bool HasMore => _nextCursor is not null;

    /// <inheritdoc />
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            var page = await _pageLoader(null, PageSize, cancellationToken);
            ClearItems();
            foreach (var item in page.Items)
                Add(item);
            _nextCursor = page.NextCursor;
            _hasLoaded = true;
            TotalCount = page.TotalCount;
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(HasMore)));
        }
        finally
        {
            _loadGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task LoadNextAsync(CancellationToken cancellationToken = default)
    {
        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            if (!_hasLoaded || _nextCursor is null)
                return;
            var page = await _pageLoader(_nextCursor, PageSize, cancellationToken);
            foreach (var item in page.Items)
                Add(item);
            while (Count > MaximumItems)
                RemoveAt(0);
            _nextCursor = page.NextCursor;
            TotalCount = page.TotalCount;
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(HasMore)));
        }
        finally
        {
            _loadGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No wait handle is allocated. In-flight async loads may still release this managed gate
        // after the window closes; disposing it here would turn cancellation into an unhandled error.
        GC.SuppressFinalize(this);
    }
}
