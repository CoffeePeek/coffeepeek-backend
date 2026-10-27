using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;

/// <summary>
/// The ordered OnShelf bags of one owner. Every change leaves positions contiguous (0…Count-1)
/// and the shelf at no more than <see cref="Capacity"/> bags. Callers serialize per-owner writes.
/// </summary>
public sealed class ShelfLineup
{
    public const int Capacity = 200;

    private readonly Guid _userId;
    private readonly List<ShelfBag> _bags;

    public ShelfLineup(Guid userId, IEnumerable<ShelfBag> onShelfBags)
    {
        _userId = userId;
        _bags = onShelfBags.Where(b => b.UserId == userId && b.Status == ShelfBagStatus.OnShelf)
            .OrderBy(b => b.Position ?? int.MaxValue).ThenBy(b => b.AddedAtUtc).ThenBy(b => b.Id).ToList();
        Renumber();
    }

    public IReadOnlyList<ShelfBag> Bags => _bags;

    public void EnsureRoom()
    {
        if (_bags.Count >= Capacity) throw new ConflictException($"The shelf already holds {Capacity} bags.");
    }

    public void AddToFront(ShelfBag bag)
    {
        Own(bag);
        if (bag.Status != ShelfBagStatus.OnShelf || _bags.Contains(bag)) throw new ConflictException("The bag is already placed.");
        EnsureRoom();
        _bags.Insert(0, bag);
        Renumber();
    }

    public void Reorder(IReadOnlyList<Guid>? bagIds)
    {
        var byId = _bags.ToDictionary(b => b.Id);
        if (bagIds == null || bagIds.Count != _bags.Count || bagIds.Distinct().Count() != bagIds.Count
            || bagIds.Any(id => !byId.ContainsKey(id)))
            throw new ConflictException("The shelf changed; reload it and retry.");
        var ordered = bagIds.Select(id => byId[id]).ToList();
        _bags.Clear();
        _bags.AddRange(ordered);
        Renumber();
    }

    public void Finish(ShelfBag bag, int? rating, bool? buyAgain, DateTime nowUtc)
    {
        Own(bag);
        ShelfBag.ValidateRating(rating);
        if (bag.Status != ShelfBagStatus.OnShelf || !_bags.Remove(bag))
            throw new ConflictException("Only a bag on the shelf can be finished.");
        bag.MarkFinished(rating, buyAgain, nowUtc);
        Renumber();
    }

    public void Restore(ShelfBag bag, int? position, DateTime nowUtc)
    {
        Own(bag);
        if (bag.Status != ShelfBagStatus.Finished) throw new ConflictException("Only an archived bag can be restored.");
        EnsureRoom();
        bag.MarkRestored(nowUtc);
        _bags.Insert(Math.Clamp(position ?? 0, 0, _bags.Count), bag);
        Renumber();
    }

    /// <summary>Takes a deleted bag off the shelf; archived bags leave positions unchanged.</summary>
    public void Remove(ShelfBag bag)
    {
        Own(bag);
        if (_bags.Remove(bag)) Renumber();
    }

    private void Own(ShelfBag bag)
    {
        if (!bag.IsOwnedBy(_userId)) throw new DomainException("The bag belongs to another shelf.");
    }

    private void Renumber()
    {
        for (var i = 0; i < _bags.Count; i++)
            if (_bags[i].Position != i) _bags[i].MoveTo(i);
    }
}
