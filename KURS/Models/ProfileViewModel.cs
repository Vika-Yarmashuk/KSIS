using System;

namespace Millioner.Models
{
    public class ProfileViewModel
    {
        public string UserName { get; set; }
        public int TotalScore { get; set; }
        public string AvatarPath { get; set; }
        public DateTime RegistrationDate { get; set; }
        public int GamesPlayed { get; set; }
        public int GamesWon { get; set; }
    }
}