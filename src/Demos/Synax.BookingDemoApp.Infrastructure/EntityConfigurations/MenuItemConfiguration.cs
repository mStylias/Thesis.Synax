using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Synax.BookingDemoApp.Domain.Entities;

namespace Synax.BookingDemoApp.Infrastructure.EntityConfigurations;

public class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("menu_items");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Price).HasPrecision(10, 2);

        builder.Property(m => m.Category)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne<Restaurant>()
            .WithMany()
            .HasForeignKey(m => m.RestaurantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
