using CoffeePeek.Account.Domain;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Account.Persistence.Configuration;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.About)
            .HasMaxLength(600);

        builder.Property(u => u.Username)
            .HasConversion(v => v.Value, v => Username.Create(v))
            .HasMaxLength(BusinessConstants.MaxUserNameLength)
            .IsRequired();

        builder.HasIndex(u => u.Username).IsUnique();
        // Prevent two requests that read the same cooldown from both renaming, including with slugs disabled.
        builder.Property(u => u.UsernameChangedAtUtc).IsConcurrencyToken();

        builder.Property(u => u.PhoneNumber)
            .HasConversion(v => v != null ? v.Value : null, v => v != null ? PhoneNumber.Create(v) : null)
            .HasMaxLength(BusinessConstants.MaxPhoneNumberLength);

        builder.OwnsOne(u => u.Credentials, cb =>
        {
            cb.Property(c => c.Email)
                .HasConversion(v => v.Value, v => Email.Create(v))
                .HasColumnName(nameof(UserCredential.Email))
                .HasMaxLength(255)
                .IsRequired();

            cb.Property(c => c.PasswordHash).HasColumnName(nameof(UserCredential.PasswordHash)).IsRequired();
            cb.Property(c => c.EmailConfirmed).HasColumnName(nameof(UserCredential.EmailConfirmed));
            cb.Property(c => c.OAuthProvider).HasColumnName(nameof(UserCredential.OAuthProvider)).HasMaxLength(BusinessConstants.MaxOAuthProviderLength);
            cb.Property(c => c.ProviderId).HasColumnName(nameof(UserCredential.ProviderId)).HasMaxLength(BusinessConstants.MaxIdProviderLength);
            cb.Property(c => c.EmailConfirmationToken).HasColumnName(nameof(UserCredential.EmailConfirmationToken)).HasMaxLength(BusinessConstants.MaxEmailConfirmationTokenLength);
            cb.Property(c => c.EmailConfirmationExpiresAt).HasColumnName(nameof(UserCredential.EmailConfirmationExpiresAt));
            cb.Property(c => c.PasswordResetToken).HasColumnName(nameof(UserCredential.PasswordResetToken)).HasMaxLength(BusinessConstants.MaxPasswordResetTokenLength);
            cb.Property(c => c.PasswordResetExpiresAt).HasColumnName(nameof(UserCredential.PasswordResetExpiresAt));

            cb.HasIndex(c => c.Email).IsUnique();
        });

        builder.OwnsOne(u => u.Statistics, sb =>
        {
            sb.Property(s => s.CheckInCount).HasColumnName(nameof(UserStatistics.CheckInCount));
            sb.Property(s => s.AddedShopsCount).HasColumnName(nameof(UserStatistics.AddedShopsCount));
            sb.Property(s => s.StatisticUpdatedAtUtc).HasColumnName(nameof(UserStatistics.StatisticUpdatedAtUtc));
        });

        builder.HasMany(u => u.RefreshTokens)
            .WithOne(rt => rt.User)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.Roles)
            .WithMany(r => r.Users)
            .UsingEntity<Dictionary<string, object>>(
                "UserRoles",
                j => j.HasOne<Role>().WithMany().HasForeignKey("RoleId"),
                j => j.HasOne<User>().WithMany().HasForeignKey("UserId")
            );

    }
}
