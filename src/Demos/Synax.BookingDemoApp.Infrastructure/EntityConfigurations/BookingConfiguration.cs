using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Synax.BookingDemoApp.Domain.Entities;

namespace Synax.BookingDemoApp.Infrastructure.EntityConfigurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.CustomerName).IsRequired().HasMaxLength(200);
        builder.Property(b => b.CustomerEmail).IsRequired().HasMaxLength(200);
        builder.Property(b => b.Notes).HasMaxLength(500);
        builder.Property(b => b.Date).IsRequired();
        builder.Property(b => b.PartySize).IsRequired();

        builder.Property(b => b.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne<Restaurant>()
            .WithMany()
            .HasForeignKey(b => b.RestaurantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Table>()
            .WithMany()
            .HasForeignKey(b => b.TableId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.CreatedBy)
            .WithMany()
            .HasForeignKey(b => b.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.ModifiedBy)
            .WithMany()
            .HasForeignKey(b => b.ModifiedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => new { b.RestaurantId, b.Date });
    }
}
