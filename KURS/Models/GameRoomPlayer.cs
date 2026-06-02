using Millioner.Models;

namespace Millioner.Models
{
    public class GameRoomPlayer
    {
        public int Id { get; set; }
        public int GameRoomId { get; set; }
        public string UserId { get; set; } = "";
        public bool IsAlive { get; set; } = true;
        public int Score { get; set; } = 0;

        // Навигационные свойства
        public virtual GameRoom? GameRoom { get; set; }
        public virtual ApplicationUser? User { get; set; }
    }
}