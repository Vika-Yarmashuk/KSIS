namespace Millioner.Models
{
    public class GameSession
    {
        public int Id { get; set; }
        public string UserId { get; set; } = "";
        public int ScoreEarned { get; set; }   // выигрыш в этой игре
        public int QuestionsAnswered { get; set; }
        public DateTime PlayedAt { get; set; }
        public ApplicationUser? User { get; set; }
    }
}
