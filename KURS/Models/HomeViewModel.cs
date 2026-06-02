using System.Collections.Generic;

namespace Millioner.Models
{
    public class HomeViewModel
    {
        public ApplicationUser CurrentUser { get; set; }
        public List<LeaderboardEntry> Leaderboard { get; set; }
    }

    public class LeaderboardEntry
    {
        public string UserName { get; set; }
        public string AvatarPath { get; set; }
        public int TotalScore { get; set; }
    }
}