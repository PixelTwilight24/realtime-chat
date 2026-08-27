using api.Models;
using api.Services;
using Microsoft.EntityFrameworkCore;

namespace api.Data;

// Dev-only convenience data so the chat has other people to talk to right after cloning
// the repo. Every seed account shares one password so it's easy to log in and test with.
public static class DbSeeder
{
    private const string SeedPassword = "Pass1234@";

    private static readonly (string Name, string Email, string Gender)[] SeedUsers =
    [
        ("Alice Johnson", "alice.johnson@example.com", "Female"),
        ("Bob Smith", "bob.smith@example.com", "Male"),
        ("Charlie Davis", "charlie.davis@example.com", "Male"),
        ("Diana Prince", "diana.prince@example.com", "Female"),
        ("Ethan Hunt", "ethan.hunt@example.com", "Male"),
    ];

    public static async Task SeedAsync(ChatDbContext db, CryptoHelper crypto)
    {
        foreach (var (name, email, gender) in SeedUsers)
        {
            var exists = await db.Users.AnyAsync(u => u.Email == email);
            if (exists) continue;

            db.Users.Add(new User
            {
                Name = name,
                Email = email,
                Gender = gender,
                PasswordHash = crypto.HashPassword(SeedPassword),
                Avatar = $"https://i.pravatar.cc/150?u={Uri.EscapeDataString(email)}",
            });
        }

        await db.SaveChangesAsync();
    }
}
