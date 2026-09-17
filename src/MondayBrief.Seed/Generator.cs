namespace MondayBrief.Seed;

/// <summary>Builds a year of orders and traffic from the demand model. Deterministic for a given seed.</summary>
public sealed class Generator
{
    // Baselines per ordinary day (before day-of-week, montha and event multipliers).
    private const double CoffeeOnlyTransactions = 44;
    private const double RetailOnlyTransactions = 24;
    private const double MixedTransactions = 12;
    private const double BaseSessions = 400;

    // Under-dispersed online order counts keep 7-day conversion from crossing 2% on ordinary weeks.
    private const double OnlineOrderDispersion = 0.35;

    private static readonly double[] CoffeeLineCounts = [0.65, 0.30, 0.05];
    private static readonly double[] RetailLineCounts = [0.60, 0.28, 0.10, 0.02];
    private static readonly double[] MixedRetailLineCounts = [0.75, 0.25];
    private static readonly double[] OnlineLineCounts = [0.35, 0.35, 0.20, 0.10];
    private static readonly double[] CoffeeQuantities = [0.85, 0.15];
    private static readonly double[] RetailQuantities = [0.85, 0.12, 0.03];

    // Hour weights starting at 7:00 (in-store) and 0:00 (online). Online skips 2:00 so no order
    // lands in the spring-forward gap.
    private static readonly double[] CoffeeHourWeights = [9, 10, 8, 6, 4, 4, 3, 2, 2, 1, 1];
    private static readonly double[] RetailHourWeights = [1, 2, 4, 6, 7, 7, 7, 6, 6, 5, 3];
    private static readonly double[] OnlineHourWeights = [2, 1, 0, 1, 1, 2, 3, 4, 5, 6, 6, 6, 7, 7, 6, 6, 6, 6, 7, 8, 9, 9, 7, 4];

    private readonly SeededRng _rng;
    private readonly ProductPicker _coffee;
    private readonly ProductPicker _inStoreRetail;
    private readonly ProductPicker _online;
    private long _nextOnlineOrder;

    public Generator(SeededRng rng)
    {
        _rng = rng;
        _coffee = new ProductPicker(Catalog.CoffeeProducts, p => p.InStoreWeight, rng);
        _inStoreRetail = new ProductPicker(Catalog.RetailProducts, p => p.InStoreWeight, rng);
        _online = new ProductPicker(Catalog.OnlineProducts, p => p.OnlineWeight, rng);
    }

    public Simulation Run()
    {
        var orders = new List<SimOrder>();
        var traffic = new List<SimTrafffic>();

        for (var d = PlantedEvents.DataStart; d <= PlantedEvents.DataEnd; d = d.AddDays(1))
        {
            orders.AddRange(InStoreDay(d));

            var dayTraffic = TrafficDay(d);
            traffic.Add(dayTraffic);
            orders.AddRange(OnlineDay(d, dayTraffic.Sessions));
        }

        return new Simulation(orders, traffic);
    }

    private List<SimOrder> InStoreDay(DateOnly d)
    {
        var result = new List<SimOrder>();
        var eventMultiplier = DemandModel.InStoreEvent(d);
        if (eventMultiplier <= 0)
        {
            return result;
        }

        var scale = DemandModel.InStoreDayOfWeek(d.DayOfWeek) * DemandModel.InStoreMonth(d.Month) * eventMultiplier;
        var hours = DemandModel.StoreHours(d);

        var coffeeOnly = _rng.Poisson(CoffeeOnlyTransactions * scale);
        var retailOnly = _rng.Poisson(RetailOnlyTransactions * scale);
        var mixed = _rng.Poisson(MixedTransactions * scale);

        for (var i = 0; i < coffeeOnly; i++)
        {
            var lines = _coffee.Pick(d, _rng.PickCount(CoffeeLineCounts), CoffeeQuantities);
            result.Add(InStoreOrder(d, hours, CoffeeHourWeights, lines));
        }

        for (var i = 0; i < retailOnly; i++)
        {
            var lines = _inStoreRetail.Pick(d, _rng.PickCount(RetailLineCounts), RetailQuantities);
            result.Add(InStoreOrder(d, hours, RetailHourWeights, lines));
        }

        for (var i = 0; i < mixed; i++)
        {
            var lines = _coffee.Pick(d, 1, CoffeeQuantities);
            lines.AddRange(_inStoreRetail.Pick(d, _rng.PickCount(MixedRetailLineCounts), RetailQuantities));
            result.Add(InStoreOrder(d, hours, RetailHourWeights, lines));
        }

        result.Sort((a, b) => a.LocalTime.CompareTo(b.LocalTime));
        for (var i = 0; i < result.Count; i++)
        {
            result[i].ExternalId = $"T{d:yyMMdd}-{i + 1:0000}";
            result[i].Register = _rng.NextDouble() < 0.6 ? "REG1" : "REG2"; 
        }

        return result;
    }

    private SimOrder InStoreOrder(DateOnly d, DemandModel.Hours hours, double[] hourWeightsFrom7, List<SimLine> lines)
    {
        Span<double> weights = stackalloc double[hourWeightsFrom7.Length];
        for (var i = 0; i < weights.Length; i++)
        {
            var hour = 7 + i;
            weights[i] = hour >= hours.Open && hour < hours.Close ? hourWeightsFrom7[i] : 0;
        }

        var pickedHour = 7 + _rng.PickIndex(weights);
        var time = d.ToDateTime(new TimeOnly(pickedHour, _rng.Next(0, 60), _rng.Next(0, 60)));

        return new SimOrder
        {
            Channel = SimChannel.InStore,
            ExternalId = "",
            LocalTime = time,
            BusinessDate = d,
            Lines = lines,
        };
    }

    private SimTrafffic TrafficDay(DateOnly d)
    {
        var expected = BaseSessions
            * DemandModel.OnlineDayOfWeek(d.DayOfWeek)
            * DemandModel.OnlineMonth(d.Month)
            * DemandModel.SessionEvent(d);
        
        var sessions = Math.Max(0, (int)Math.Round(expected * (1 + _rng.Normal(0, 0.06))));
        var users = (int)Math.Round(sessions * (0.78 + _rng.Normal(0, 0.02)));
        var views = (int)Math.Round(sessions * (3.1 + _rng.Normal(0, 0.15)));
        return new SimTrafffic(d, sessions, users, views);
    }

    private List<SimOrder> OnlineDay(DateOnly d, int sessions)
    {
        var result = new List<SimOrder>();
        var orderToSessionRatio = DemandModel.OnlineOrderEvent(d) / DemandModel.SessionEvent(d);
        var expectedOrders = sessions * DemandModel.ConversionRate(d) * orderToSessionRatio;
        var count = _rng.Count(expectedOrders, OnlineOrderDispersion);

        for (var i = 0; i < count; i++)
        {
            var lines = _online.Pick(d, _rng.PickCount(OnlineLineCounts), RetailQuantities);
            var hour = _rng.PickIndex(OnlineHourWeights);
            var time = d.ToDateTime(new TimeOnly(hour, _rng.Next(0, 60), _rng.Next(0, 60)));
            result.Add(new SimOrder
            {
                Channel = SimChannel.Online,
                ExternalId = "",
                LocalTime = time,
                BusinessDate = d,
                Lines = lines,
            });
        }

        result.Sort((a, b) => a.LocalTime.CompareTo(b.LocalTime));
        foreach (var order in result)
        {
            order.ExternalId = (5_001_000 + _nextOnlineOrder++). ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return result;
    }
}