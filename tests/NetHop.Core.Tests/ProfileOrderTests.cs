using NetHop.Core.Profiles;

namespace NetHop.Core.Tests;

public class ProfileOrderTests
{
    private static readonly Profile Home = new() { Name = "Home" };
    private static readonly Profile Office = new() { Name = "Office" };
    private static readonly Profile Lab = new() { Name = "Lab" };

    [Fact]
    public void Without_an_order_profiles_are_sorted_by_name()
    {
        var sorted = ProfileOrder.Sort([Office, Lab, Home], []);

        Assert.Equal(["Home", "Lab", "Office"], sorted.Select(p => p.Name));
    }

    [Fact]
    public void Saved_order_comes_first_then_new_profiles_by_name()
    {
        var sorted = ProfileOrder.Sort([Home, Lab, Office], [Office.Id, Guid.NewGuid(), Home.Id]);

        Assert.Equal(["Office", "Home", "Lab"], sorted.Select(p => p.Name));
    }

    [Fact]
    public void Duplicate_ids_keep_their_first_position()
    {
        var sorted = ProfileOrder.Sort([Home, Office], [Office.Id, Home.Id, Office.Id]);

        Assert.Equal(["Office", "Home"], sorted.Select(p => p.Name));
    }
}
