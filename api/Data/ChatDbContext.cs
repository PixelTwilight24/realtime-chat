using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Data;

public class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();

    public DbSet<GroupMessage> GroupMessages => Set<GroupMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Message>()
            .HasOne(m => m.Sender)
            .WithMany(u => u.SentMessages)
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Message>()
            .HasOne(m => m.Receiver)
            .WithMany(u => u.ReceivedMessages)
            .HasForeignKey(m => m.ReceiverId)
            .OnDelete(DeleteBehavior.Restrict);

        // Group -> User (creator) is Restrict, same reasoning as Message.Sender/Receiver above.
        // GroupMember/GroupMessage -> Group are Cascade (deleting a group cleans up its rows);
        // that's safe only because Group -> User is Restrict, so there's no cascade path back
        // through Group that could collide with the direct GroupMember/GroupMessage -> User FKs
        // (both also Restrict) and trigger SQL Server's multiple-cascade-path error.
        modelBuilder.Entity<Group>()
            .HasOne(g => g.CreatedBy)
            .WithMany(u => u.CreatedGroups)
            .HasForeignKey(g => g.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GroupMember>()
            .HasOne(gm => gm.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(gm => gm.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GroupMember>()
            .HasOne(gm => gm.User)
            .WithMany(u => u.GroupMemberships)
            .HasForeignKey(gm => gm.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GroupMember>()
            .HasIndex(gm => new { gm.GroupId, gm.UserId })
            .IsUnique();

        modelBuilder.Entity<GroupMessage>()
            .HasOne(gm => gm.Group)
            .WithMany(g => g.Messages)
            .HasForeignKey(gm => gm.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GroupMessage>()
            .HasOne(gm => gm.Sender)
            .WithMany(u => u.SentGroupMessages)
            .HasForeignKey(gm => gm.SenderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
