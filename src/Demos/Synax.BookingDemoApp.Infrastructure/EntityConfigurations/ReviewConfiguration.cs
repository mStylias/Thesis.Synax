using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Synax.BookingDemoApp.Domain.Entities;

namespace Synax.BookingDemoApp.Infrastructure.EntityConfigurations;

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Rating).IsRequired();
        builder.Property(r => r.Comment).IsRequired().HasMaxLength(1000);
        builder.Property(r => r.ReviewerEmail).IsRequired().HasMaxLength(200);

        builder.HasOne<Restaurant>()
            .WithMany()
            .HasForeignKey(r => r.RestaurantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.CreatedBy)
            .WithMany()
            .HasForeignKey(r => r.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
