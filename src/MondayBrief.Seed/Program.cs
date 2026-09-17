using MondayBrief.Seed;

// Temporary probe for Step 9.6. Replaced in Part 2.
DateOnly[] days =
[
    new(2025, 10, 15), // ordinary Wednesday
    PlantedEvents.BlackFriday,
    PlantedEvents.Christmas,
    new(2026, 2, 14),   // Mardi Gras peak (Saturday)
    PlantedEvents.FatTuesday,
    PlantedEvents.AshWednesday,
    PlantedEvents.StormPrepDay,
    PlantedEvents.StormClosedStart,
    new(2026, 7, 12),   // day before conversion drop
    new(2026, 7, 13),   // conversion drop
];

var candle = Catalog.All.Single(p => p.Sku == PlantedEvents.DecliningSku);

Console.WriteLine($"{"Date",-12}{"Day",-10}{"InStore",8}{"Sessions",10}{"Orders",8}{"Conv",7}");
foreach (var d in days)
{
    var inStore = DemandModel.InStoreDayOfWeek(d.DayOfWeek) * DemandModel.InStoreMonth(d.Month) * DemandModel.InStoreEvent(d);
    Console.WriteLine(
    $"{d:yyyy-MM-dd} {d.DayOfWeek,-10}{inStore,8:0.00}{DemandModel.SessionEvent(d),10:0.00}" +
    $"{DemandModel.OnlineOrderEvent(d),8:0.00}{DemandModel.ConversionRate(d),7:P1}");
}

Console.WriteLine();
Console.WriteLine("Candle weight multiplier:");
foreach (var d in new DateOnly[] { new(2026, 5, 31), new(2026, 6, 30), new(2026, 7, 31), new(2026, 8, 30) })
{
    Console.WriteLine($"    {d:yyyy-MM-dd}  {DemandModel.ProductMultiplier(candle, d):0.00}");
}
