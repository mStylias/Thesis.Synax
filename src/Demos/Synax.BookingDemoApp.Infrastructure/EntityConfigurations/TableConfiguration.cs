using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Synax.BookingDemoApp.Domain.Entities;

namespace Synax.BookingDemoApp.Infrastructure.EntityConfigurations;

public class TableConfiguration : IEntityTypeConfiguration<Table>
{
    public void Configure(EntityTypeBuilder<Table> builder)
    {
        builder.ToTable("tables");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Number).IsRequired();
        builder.Property(t => t.Seats).IsRequired();

        builder.Property(t => t.Location)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasIndex(t => new { t.RestaurantId, t.Number });
    }
}
