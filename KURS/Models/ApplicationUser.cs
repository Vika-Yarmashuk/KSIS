using Microsoft.AspNetCore.Identity;

namespace Millioner.Models
{
    public class ApplicationUser : IdentityUser
    {
        public int TotalScore { get; set; } = 0;
        public string AvatarPath { get; set; } = "/images/avatars/avatar1.png";
        public DateTime RegistrationDate { get; set; } = DateTime.UtcNow;
        public int GamesPlayed { get; set; } = 0;   // общее количество сыгранных игр
        public int GamesWon { get; set; } = 0;      // количество побед (в одиночных играх – ответ на все вопросы, в многопользовательских – победа в комнате)
    }
}
