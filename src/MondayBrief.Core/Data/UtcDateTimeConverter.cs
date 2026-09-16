using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MondayBrief.Core.Data;

/// <summary>SQLite stores DateTime as text without a Kind. Everything we store is UTC, so stamp it back on read.</summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {       
    }
}