using Microsoft.EntityFrameworkCore;

/// <summary>Builds community ranking from individual point events.</summary>
public sealed class CommunityService(AppDbContext db)
{
    /// <summary>Returns the 20 highest scoring users with deterministic tie breaks.</summary>
    public async Task<object> Leaderboard()
    {
        var scores = await db.PointEvents.AsNoTracking().GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Points = g.Sum(p => p.Points), Answers = g.Count() }).ToListAsync();
        var users = await db.Users.AsNoTracking().Where(u => scores.Select(s => s.UserId).Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName }).ToListAsync();
        return (from s in scores join u in users on s.UserId equals u.Id
            orderby s.Points descending, s.Answers descending, u.DisplayName
            select new { display_name = u.DisplayName, points = s.Points, answers = s.Answers }).Take(20).ToList();
    }
}
