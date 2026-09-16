using Microsoft.EntityFrameworkCore;
using MondayBrief.Core.Entities;

namespace MondayBrief.Core.Data;

public sealed class MondayBriefDbContext(DbContextOptions<MondayBriefDbContext> options) : DbContext(options)
{
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<DailyTraffic> DailyTraffic => Set<DailyTraffic>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<Brief> Briefs => Set<Brief>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Channel>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(64);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasData(
                new Channel { Id = ChannelIds.InStore, Code = "InStore", Name = "In-store"},
                new Channel { Id = ChannelIds.Online, Code = "Online", Name = "Online store"});
        });

        b.Entity<Product>(e => 
        {
            e.Property(x => x.Sku).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Category).HasMaxLength(32);
            e.HasIndex(x => x.Sku).IsUnique();
            e.HasIndex(x => x.Category);
        });

        b.Entity<Order>(e =>
        {
            e.Property(x => x.SourceSystem).HasMaxLength(16);
            e.Property(x => x.ExternalId).HasMaxLength(64);
            e.HasIndex(x => new { x.SourceSystem, x.ExternalId }).IsUnique();
            e.HasIndex(x => new { x.BusinessDate, x.ChannelId });
            e.HasOne(x => x.Channel)
                .WithMany(c => c.Orders)
                .HasForeignKey(x => x.ChannelId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OrderLine>(e => 
        {
            e.HasOne(x => x.Order)
                .WithMany(o => o.Lines)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product)
                .WithMany(p => p.OrderLines)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ProductId);
        });

        b.Entity<DailyTraffic>(e => 
        {
            e.ToTable("DailyTraffic");
            e.HasKey(x => x.Date);
            e.Property(x => x.Date).ValueGeneratedNever();
        });

        b.Entity<AlertRule>(e => 
        {
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.Key).IsUnique();
            e.HasData(
                new AlertRule
                {
                    Id = 1, Key = "weekly-revenue-drop", Name = "Weekly revenue down more than 20%",
                    Kind = AlertRuleKind.RevenueChangePct, Threshold = -20, WindowDays = 7, MinBaseline = 0,
                },
                new AlertRule
                {
                    Id = 2, Key = "conversion-below-2pct", Name = "7-day online conversion below 2%",
                    Kind = AlertRuleKind.ConversionRateBelow, Threshold = 2.0, WindowDays = 7, MinBaseline = 500,
                },
                new AlertRule
                {
                    Id = 3, Key = "product-units-drop", Name = "Product units down more than 30% over 4 weeks",
                    Kind = AlertRuleKind.ProductUnitsChangePct, Threshold = -30, WindowDays = 28, MinBaseline = 60,
                });
        });

        b.Entity<Brief>(e => 
        {
            e.Property(x => x.Model).HasMaxLength(64);
            e.HasIndex(x => x.WeekStart).IsUnique();
        });
    }
}