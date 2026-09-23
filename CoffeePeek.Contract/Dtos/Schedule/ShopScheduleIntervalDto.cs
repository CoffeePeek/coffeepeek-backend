namespace CoffeePeek.Contract.Dtos.Schedule;

public class ShopScheduleIntervalDto
{
    /// <summary>Opening time in UTC.</summary>
    public TimeSpan OpenTime { get; set; }

    /// <summary>
    /// Closing time in UTC. A value earlier than <see cref="OpenTime"/> means the interval ends
    /// on the following UTC day.
    /// </summary>
    public TimeSpan CloseTime { get; set; }
}
