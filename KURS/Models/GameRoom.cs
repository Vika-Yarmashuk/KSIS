using Millioner.Models;

namespace Millioner.Models
{
    public class GameRoom
    {
        public int Id { get; set; }
        public string RoomCode { get; set; } = "";
        public string OwnerId { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; } = true;
        public int CurrentQuestionIndex { get; set; } = 0;

        // Навигационное свойство: один GameRoom может иметь много GameRoomPlayer
        public virtual ICollection<GameRoomPlayer> GameRoomPlayers { get; set; } = new List<GameRoomPlayer>();
    }
}
