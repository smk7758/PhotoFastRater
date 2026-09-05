using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Infrastructure.Database.Repositories;

/// <summary>Persists normalized tags and hierarchical collections with idempotent mappings.</summary>
public sealed class LibraryOrganizationRepository(IDbContextFactory<PhotoDbContext> contextFactory)
{
    /// <summary>Lists tags by display name without tracking library-sized relationship graphs.</summary>
    public async Task<IReadOnlyList<PhotoTag>> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.PhotoTags.AsNoTracking().OrderBy(tag => tag.Name).ToListAsync(cancellationToken);
    }

    /// <summary>Lists collection nodes; callers construct the display hierarchy from parent IDs in O(n).</summary>
    public async Task<IReadOnlyList<PhotoCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.PhotoCollections.AsNoTracking()
            .OrderBy(collection => collection.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Adds one tag to a bounded photo selection and ignores mappings that already exist.</summary>
    public async Task AddTagAsync(
        string name,
        IReadOnlyCollection<int> photoIds,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeName(name);
        var ids = ValidateSelection(photoIds);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var tag = await context.PhotoTags.SingleOrDefaultAsync(item => item.NormalizedName == normalized, cancellationToken);
        if (tag is null)
        {
            tag = new PhotoTag { Name = name.Trim(), NormalizedName = normalized };
            context.PhotoTags.Add(tag);
            await context.SaveChangesAsync(cancellationToken);
        }

        var existing = await context.PhotoTagMappings
            .Where(mapping => mapping.TagId == tag.Id && ids.Contains(mapping.PhotoId))
            .Select(mapping => mapping.PhotoId)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet();
        context.PhotoTagMappings.AddRange(ids.Where(id => !existingSet.Contains(id)).Select(id => new PhotoTagMapping
        {
            PhotoId = id,
            TagId = tag.Id,
            AddedUtc = DateTime.UtcNow
        }));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Creates one sibling-unique collection beneath an optional parent.</summary>
    public async Task<PhotoCollection> CreateCollectionAsync(
        string name,
        int? parentId,
        CancellationToken cancellationToken = default)
    {
        var trimmed = ValidateName(name);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (parentId.HasValue && !await context.PhotoCollections.AnyAsync(item => item.Id == parentId, cancellationToken))
            throw new KeyNotFoundException($"Parent collection {parentId} was not found.");
        if (await context.PhotoCollections.AnyAsync(item => item.ParentId == parentId && item.Name == trimmed, cancellationToken))
            throw new InvalidOperationException("同じ階層に同名のコレクションがあります。");

        var collection = new PhotoCollection { Name = trimmed, ParentId = parentId };
        context.PhotoCollections.Add(collection);
        await context.SaveChangesAsync(cancellationToken);
        return collection;
    }

    /// <summary>Adds a bounded selection to one collection in a transaction and remains idempotent.</summary>
    public async Task AddToCollectionAsync(
        int collectionId,
        IReadOnlyCollection<int> photoIds,
        CancellationToken cancellationToken = default)
    {
        var ids = ValidateSelection(photoIds);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.PhotoCollections.AnyAsync(item => item.Id == collectionId, cancellationToken))
            throw new KeyNotFoundException($"Collection {collectionId} was not found.");
        var existing = await context.PhotoCollectionMappings
            .Where(mapping => mapping.CollectionId == collectionId && ids.Contains(mapping.PhotoId))
            .Select(mapping => mapping.PhotoId)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet();
        context.PhotoCollectionMappings.AddRange(ids.Where(id => !existingSet.Contains(id)).Select(id => new PhotoCollectionMapping
        {
            PhotoId = id,
            CollectionId = collectionId,
            AddedUtc = DateTime.UtcNow
        }));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeName(string name) => ValidateName(name).ToUpperInvariant();

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(name), "名前は128文字以内です。");
        return trimmed;
    }

    private static int[] ValidateSelection(IReadOnlyCollection<int> photoIds)
    {
        ArgumentNullException.ThrowIfNull(photoIds);
        if (photoIds.Count is < 1 or > 1280 || photoIds.Any(id => id <= 0))
            throw new ArgumentOutOfRangeException(nameof(photoIds), "一括操作は1～1,280件の有効な写真IDが必要です。");
        return photoIds.Distinct().ToArray();
    }
}
