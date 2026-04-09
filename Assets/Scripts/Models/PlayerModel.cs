/// <summary>
/// Global player state — economy (points) and gear inventory.
/// Pure game logic, no MonoBehaviour.
/// </summary>
public class PlayerModel
{
    public int Points { get; set; }

    public PlayerModel(int startingPoints)
    {
        Points = startingPoints;
    }

    /// <summary>Try to purchase a gear item. Returns true and deducts points on success.</summary>
    public bool TryPurchase(GearItem item)
    {
        if (item == null || Points < item.Cost) return false;
        Points -= item.Cost;
        return true;
    }

    /// <summary>Refund a gear item's cost.</summary>
    public void Refund(GearItem item)
    {
        if (item != null)
            Points += item.Cost;
    }
}
