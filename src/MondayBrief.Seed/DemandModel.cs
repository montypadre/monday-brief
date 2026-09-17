using static MondayBrief.Seed.PlantedEvents;

namespace MondayBrief.Seed;

/// <summary>
/// Multipliers that turn a flat baseline into a believable year. Baselines (orders per day, sessions per day) live in Generator.
/// </summary>
public static class DemandModel
{
    public readonly record struct Hours(int Open, int Close);

    public static bool IsInStoreClosed(DateOnly d) =>
        d == Thanksgiving || d == Christmas || InRange(d, StormClosedStart, StormClosedEnd);
    
    public static Hours StoreHours(DateOnly d)
    {
        if (d == StormPrepDay || d == ChrsitmasEve)
        {
            return new Hours(7, 14);
        }

        if (d == StormCleanupDay)
        {
            return new Hours(10, 18);
        }

        return d.DayOfWeek == DayOfWeek.Sunday ? new Hours(8, 17) : new Hours(7, 18);
    }

    public static double InStoreDayOfWeek(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => 0.85,
        DayOfWeek.Tuesday => 0.85,
        DayOfWeek.Wednesday => 0.90,
        DayOfWeek.Thursday => 0.95,
        DayOfWeek.Friday => 1.10,
        DayOfWeek.Saturday => 1.40,
        _ => 1.00,
    };

    public static double InStoreMonth(int month) => month switch
    {
        9 => 0.95,
        1 => 0.90,
        3 or 4 or 5 => 1.05,
        7 => 1.03,
        8 => 1.05,
        _ => 1.00,
    };

    /// <summary>Event multiplier for in-store traffic. 0 means closed.</summary>
    public static double InStoreEvent(DateOnly d)
    {
        if (IsInStoreClosed(d))
        {
            return 0;
        }

        // Weather shoulders.
        if (d == StormPrepDay)
        {
            return 0.60;
        }

        if (d == StormCleanupDay)
        {
            return 0.75;
        }

        // Mardi Gras.
        if (InRange(d, TwelfthNight, MardiGrasRampStart.AddDays(-1)))
        {
            return 1.05;
        }

        if (InRange(d, MardiGrasRampStart, MardiGrasPeakStart.AddDays(-1)))
        {
            return 1.05 + 0.45 * Progress(d, MardiGrasRampStart, MardiGrasPeakStart);
        }

        if (InRange(d, MardiGrasPeakStart, FatTuesday.AddDays(-1)))
        {
            return 1.80;
        }

        if (d == FatTuesday)
        {
            return 1.90;
        }

        if (d == AshWednesday)
        {
            return 0.60;
        }

        if (InRange(d, AshWednesday.AddDays(1), MardiGradHangoverEnd))
        {
            return 0.90;
        }

        // Holidays.
        if (d == BlackFriday)
        {
            return 2.60;
        }

        if (d == SmallBusinessSaturday)
        {
            return 1.80;
        }

        if (InRange(d, HolidayRampStart, HolidayPeakStart.AddDays(-1)))
        {
            return 1.10 + 0.50 * Progress(d, HolidayRampStart, HolidayPeakStart);
        }

        if (InRange(d, HolidayPeakStart, ChrsitmasEve.AddDays(-1)))
        {
            return 1.90;
        }

        if (d == ChrsitmasEve)
        {
            return 1.30;
        }

        if (InRange(d, Christmas.AddDays(1), new DateOnly(2025, 12, 31)))
        {
            return 0.80;
        }

        return 1.0;
    }

    public static double OnlineDayOfWeek(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => 1.05,
        DayOfWeek.Thursday => 0.95,
        DayOfWeek.Friday => 0.90,
        DayOfWeek.Saturday => 0.95,
        DayOfWeek.Sunday => 1.15,
        _ => 1.00,
    };

    public static double OnlineMonth(int month) => month == 1 ? 0.90 : 1.00;

    /// <summary>Event multiplier on website sessions.</summary>
    public static double SessionEvent(DateOnly d)
    {
        if (InRange(d, StormClosedStart, StormClosedEnd))
        {
            return 0.85;
        }

        return OnlineOrderEvent(d);
    }

    /// <summary>Event multiplier on online orders. Where it differs from sessions, conversion moves with it.</summary>
    public static double OnlineOrderEvent(DateOnly d)
    {
        if (InRange(d, StormClosedStart, StormClosedEnd))
        {
            return 0.60;
        }

        if (InRange(d, MardiGrasRampStart, MardiGrasPeakStart.AddDays(-1)))
        {
            return 1.0 + 0.10 * Progress(d, MardiGrasRampStart, MardiGrasPeakStart);
        }

        if (InRange(d, MardiGrasPeakStart, FatTuesday))
        {
            return 1.15;
        }

        if (d == BlackFriday)
        {
            return 2.40;
        }

        if (d == CyberMonday)
        {
            return 2.60;
        }

        if (InRange(d, HolidayRampStart, HolidayPeakStart.AddDays(-1)))
        {
            return 1.20 + 0.80 * Progress(d, HolidayRampStart, HolidayPeakStart);
        }

        if (InRange(d, HolidayPeakStart, OnlineShippingCutoff))
        {
            return 2.20;
        }

        if (InRange(d, OnlineShippingCutoff.AddDays(1), ChrsitmasEve))
        {
            return 0.90;
        }

        if (d == Christmas)
        {
            return 0.50;
        }

        if (InRange(d, Christmas.AddDays(1), new DateOnly(2025, 12, 31)))
        {
            return 0.80;
        }

        return 1.0;
    }

    public static double ConversionRate(DateOnly d) => d >= ConversionDropStart ? ConversionAfter : ConversionBefore;

    /// <summary>Per-product weight multiplier for a given day (applied in both channels).</summary>
    public static double ProductMultiplier(SeedProduct p, DateOnly d) => p.Season switch
    {
        ProductSeason.HotDrink => d.Month switch
        {
            6 or 7 or 8 or 9 => 0.94,
            3 or 4 or 5 => 0.97,
            11 or 12 or 1 or 2 => 1.10,
            _ => 1.00,
        },
        ProductSeason.ColdDrink => d.Month switch
        {
            6 or 7 or 8 or 9 => 1.50,
            3 or 4 or 5 or 10 => 1.20,
            _ => 0.70,
        },
        ProductSeason.MardiGras =>
            InRange(d, MardiGrasRampStart, FatTuesday) ? 5.0
            : InRange(d, TwelfthNight, MardiGrasRampStart.AddDays(-1)) ? 2.0
            : 1.0,
        ProductSeason.Holiday => InRange(d, HolidayRampStart, ChrsitmasEve) ? 6.0 : 0.4,
        ProductSeason.Winter => d.Month switch
        {
            11 or 12 or 1 or 2 => 2.5,
            10 => 1.3,
            _ => 1.0,
        },
        ProductSeason.Decline => d < DeclineStart
            ? 1.0
            : 1.0 - DeclineDepth * Math.Pow(Progress(d, DeclineStart, DeclineEnd), 2),
            _ => 1.0,
    };
}