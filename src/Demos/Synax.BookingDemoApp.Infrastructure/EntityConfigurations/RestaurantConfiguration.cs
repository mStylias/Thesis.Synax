using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Synax.BookingDemoApp.Domain.Entities;

namespace Synax.BookingDemoApp.Infrastructure.EntityConfigurations;

public class RestaurantConfiguration : IEntityTypeConfiguration<Restaurant>
{
    public void Configure(EntityTypeBuilder<Restaurant> builder)
    {
        builder.ToTable("restaurants");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Address).IsRequired().HasMaxLength(500);
        builder.Property(r => r.PhoneNumber).IsRequired().HasMaxLength(50);

        builder.HasOne(r => r.Cuisine)
            .WithMany()
            .HasForeignKey(r => r.CuisineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Tables)
            .WithOne()
            .HasForeignKey(t => t.RestaurantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.CreatedBy)
            .WithMany()
            .HasForeignKey(r => r.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ModifiedBy)
            .WithMany()
            .HasForeignKey(r => r.ModifiedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.DeletedBy)
            .WithMany()
            .HasForeignKey(r => r.DeletedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
