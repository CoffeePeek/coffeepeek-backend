using CoffeePeek.Account.Domain.Entities;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Account.Persistence.Configuration;

public sealed class ActivityScoreConfiguration : IEntityTypeConfiguration<ActivityScore>
{
    public void Configure(EntityTypeBuilder<ActivityScore> entity)
    {
        entity.ToTable("ActivityScores");
        entity.HasKey(x => new { x.Kind, x.SourceId });
        entity.HasIndex(x => new { x.CreditedAtUtc, x.UserId });
        entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
