namespace NetHop.Core.Profiles;

/// <summary>The order the user gave the profiles by dragging them: a list of ids, kept apart from the profile files.</summary>
public static class ProfileOrder
{
    /// <summary>
    /// Profiles in the saved order. Profiles the order does not know yet (new or imported ones) follow, by name;
    /// ids of deleted profiles are ignored.
    /// </summary>
    public static List<Profile> Sort(IEnumerable<Profile> profiles, IReadOnlyList<Guid> order)
    {
        var position = new Dictionary<Guid, int>();
        for (var i = 0; i < order.Count; i++)
            position.TryAdd(order[i], i);

        return profiles
            .OrderBy(p => position.TryGetValue(p.Id, out var index) ? index : int.MaxValue)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
