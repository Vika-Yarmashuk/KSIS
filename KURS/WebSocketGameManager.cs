using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Millioner.Data;
using Millioner.Models;

namespace Millioner.Services
{
    public static class WebSocketGameManager
    {
        private static readonly Dictionary<string, GameRoomState> _rooms = new();
        private static readonly Random _random = new();

        public class Player
        {
            public string UserId { get; set; }
            public string UserName { get; set; }
            public string AvatarPath { get; set; }         
            public int Score { get; set; }                 
            public int CorrectAnswersCount { get; set; }   
            public bool IsAlive { get; set; }
            [JsonIgnore]
            public bool HasAnswered { get; set; }
        }

        public class GameRoomState
        {
            public string RoomCode { get; set; }
            public string OwnerId { get; set; }
            public List<Player> Players { get; set; }
            public bool IsActive { get; set; }
            public int CurrentQuestionIndex { get; set; }
            public List<Question> Questions { get; set; }
            public bool IsGameOver { get; set; }
        }

        public static string CreateRoom(string ownerId, string ownerName, string ownerAvatarPath)
        {
            string roomCode;
            lock (_rooms)
            {
                do { roomCode = GenerateRoomCode(); } while (_rooms.ContainsKey(roomCode));
                var room = new GameRoomState
                {
                    RoomCode = roomCode,
                    OwnerId = ownerId,
                    Players = new List<Player>(),
                    IsActive = false,
                    CurrentQuestionIndex = 0,
                    Questions = null,
                    IsGameOver = false
                };
                room.Players.Add(new Player
                {
                    UserId = ownerId,
                    UserName = ownerName ?? "Владелец",
                    AvatarPath = ownerAvatarPath ?? "/images/avatars/avatar1.png",
                    Score = 0,
                    CorrectAnswersCount = 0,
                    IsAlive = true,
                    HasAnswered = false
                });
                _rooms[roomCode] = room;
                Console.WriteLine($"[CreateRoom] {roomCode} created, owner={ownerName}");
            }
            return roomCode;
        }

        public static bool JoinRoom(string roomCode, string userId, string userName, string avatarPath)
        {
            lock (_rooms)
            {
                roomCode = roomCode?.Trim().ToUpperInvariant();
                if (!_rooms.TryGetValue(roomCode, out var room))
                {
                    Console.WriteLine($"[JoinRoom] room {roomCode} not found");
                    return false;
                }
                if (room.IsActive) { Console.WriteLine("[JoinRoom] room already active"); return false; }
                if (room.Players.Count >= 4) { Console.WriteLine("[JoinRoom] room full"); return false; }
                if (room.Players.Any(p => p.UserId == userId))
                {
                    Console.WriteLine("[JoinRoom] player already in room");
                    return true;
                }
                room.Players.Add(new Player
                {
                    UserId = userId,
                    UserName = userName ?? "Игрок",
                    AvatarPath = avatarPath ?? "/images/avatars/avatar1.png",
                    Score = 0,
                    CorrectAnswersCount = 0,
                    IsAlive = true,
                    HasAnswered = false
                });
                Console.WriteLine($"[JoinRoom] {userName} joined {roomCode}, now {room.Players.Count} players");
                return true;
            }
        }

        public static async Task<bool> StartGameAsync(string ownerId, ApplicationDbContext dbContext)
        {
            GameRoomState room;
            lock (_rooms)
            {
                room = _rooms.Values.FirstOrDefault(r => r.OwnerId == ownerId);
                if (room == null || room.IsActive || room.Players.Count < 2)
                {
                    Console.WriteLine("[StartGame] invalid room or <2 players");
                    return false;
                }
            }

            var easy = await dbContext.Questions.Where(q => q.Difficulty == 1).OrderBy(_ => Guid.NewGuid()).Take(5).ToListAsync();
            var medium = await dbContext.Questions.Where(q => q.Difficulty == 2).OrderBy(_ => Guid.NewGuid()).Take(5).ToListAsync();
            var hard = await dbContext.Questions.Where(q => q.Difficulty == 3).OrderBy(_ => Guid.NewGuid()).Take(5).ToListAsync();

            if (easy.Count < 5 || medium.Count < 5 || hard.Count < 5)
            {
                Console.WriteLine("[StartGame] not enough questions (need 5 each difficulty)");
                return false;
            }

            var questions = easy.Concat(medium).Concat(hard).ToList();

            lock (_rooms)
            {
                room.Questions = questions;
                room.IsActive = true;
                room.CurrentQuestionIndex = 0;
                room.IsGameOver = false;
                foreach (var p in room.Players)
                {
                    p.Score = 0;
                    p.CorrectAnswersCount = 0;
                    p.IsAlive = true;
                    p.HasAnswered = false;
                }
            }
            return true;
        }

        public static async Task<(bool Correct, int Earned, bool Eliminated, bool RoundFinished, int SafeEarned, char CorrectAnswer, char SelectedAnswer)> SubmitAnswer(
            string userId, int questionId, char answer, ApplicationDbContext dbContext)
        {
            GameRoomState room;
            Player player;
            lock (_rooms)
            {
                room = _rooms.Values.FirstOrDefault(r => r.Players.Any(p => p.UserId == userId));
                if (room == null || !room.IsActive) return (false, 0, false, false, 0, '\0', '\0');
                player = room.Players.First(p => p.UserId == userId);
                if (!player.IsAlive || player.HasAnswered) return (false, 0, false, false, 0, '\0', '\0');
            }

            var question = room.Questions[room.CurrentQuestionIndex];
            if (question.Id != questionId) return (false, 0, false, false, 0, '\0', '\0');

            bool correct = (answer.ToString().ToUpper() == question.CorrectAnswer.ToString());
            int earned = 0;
            if (correct)
            {
                earned = question.Difficulty == 1 ? 100 : (question.Difficulty == 2 ? 500 : 1000);
                player.Score += earned;
                player.CorrectAnswersCount++;
            }
            else
            {
                player.IsAlive = false;
            }
            player.HasAnswered = true;

            int safeEarned = 0;
            if (!correct)
            {
                if (player.CorrectAnswersCount >= 11) safeEarned = 5000;
                else if (player.CorrectAnswersCount >= 6) safeEarned = 1000;
                else safeEarned = 0;
            }

            bool allAliveAnswered;
            lock (_rooms)
            {
                allAliveAnswered = room.Players.Where(p => p.IsAlive).All(p => p.HasAnswered);
            }

            if (allAliveAnswered)
            {
                lock (_rooms)
                {
                    foreach (var p in room.Players) p.HasAnswered = false;
                    room.CurrentQuestionIndex++;
                }

                bool gameFinished = false;
                lock (_rooms)
                {
                    var aliveCount = room.Players.Count(p => p.IsAlive);
                    if (room.CurrentQuestionIndex >= room.Questions.Count || aliveCount == 0)
                    {
                        room.IsActive = false;
                        room.IsGameOver = true;
                        gameFinished = true;
                    }
                }
                return (correct, earned, !correct, true, safeEarned, question.CorrectAnswer, answer);
            }

            return (correct, earned, !correct, false, safeEarned, question.CorrectAnswer, answer);
        }

        public static async Task<(string WinnerName, List<Player> Players)> FinishGameAsync(string roomCode, ApplicationDbContext dbContext)
        {
            var room = GetRoom(roomCode);
            if (room == null) return ("", new List<Player>());

            foreach (var player in room.Players)
            {
                int finalScore = 0;
                if (player.IsAlive && room.CurrentQuestionIndex >= room.Questions.Count)
                {
                    finalScore = player.Score;
                }
                else
                {
                    if (player.CorrectAnswersCount >= 11) finalScore = 5000;
                    else if (player.CorrectAnswersCount >= 6) finalScore = 1000;
                    else finalScore = 0;
                }
                var user = await dbContext.Users.FindAsync(player.UserId);
                if (user != null)
                {
                    user.TotalScore += finalScore;
                    user.GamesPlayed++;
                    if (player.IsAlive && room.CurrentQuestionIndex >= room.Questions.Count)
                        user.GamesWon++;
                    await dbContext.SaveChangesAsync();
                }
            }

            var winner = room.Players.OrderByDescending(p => p.Score).FirstOrDefault();
            var winnerName = winner?.UserName ?? "Неизвестно";
            var players = GetPlayersInRoom(roomCode);
          
            return (winnerName, players);
        }

        public static GameRoomState GetRoom(string roomCode)
        {
            roomCode = roomCode?.Trim().ToUpperInvariant();
            lock (_rooms)
            {
                _rooms.TryGetValue(roomCode, out var room);
                return room;
            }
        }

        public static GameRoomState GetRoomByOwner(string ownerId)
        {
            lock (_rooms)
            {
                return _rooms.Values.FirstOrDefault(r => r.OwnerId == ownerId);
            }
        }

        public static GameRoomState GetRoomByPlayer(string userId)
        {
            lock (_rooms)
            {
                return _rooms.Values.FirstOrDefault(r => r.Players.Any(p => p.UserId == userId));
            }
        }

        public static List<Player> GetPlayersInRoom(string roomCode)
        {
            roomCode = roomCode?.Trim().ToUpperInvariant();
            lock (_rooms)
            {
                if (!_rooms.TryGetValue(roomCode, out var room)) return new List<Player>();
                return room.Players.Select(p => new Player
                {
                    UserId = p.UserId,
                    UserName = p.UserName,
                    AvatarPath = p.AvatarPath,
                    Score = p.Score,
                    CorrectAnswersCount = p.CorrectAnswersCount,
                    IsAlive = p.IsAlive
                }).ToList();
            }
        }

        public static void RemovePlayer(string userId)
        {
            lock (_rooms)
            {
                foreach (var room in _rooms.Values)
                {
                    var player = room.Players.FirstOrDefault(p => p.UserId == userId);
                    if (player != null)
                    {
                        room.Players.Remove(player);
                        if (room.Players.Count == 0) _rooms.Remove(room.RoomCode);
                        break;
                    }
                }
            }
        }

        public static void RemoveRoom(string roomCode)
        {
            roomCode = roomCode?.Trim().ToUpperInvariant();
            lock (_rooms) { _rooms.Remove(roomCode); }
        }

        private static string GenerateRoomCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
            lock (_random)
            {
                return new string(Enumerable.Repeat(chars, 5).Select(s => s[_random.Next(s.Length)]).ToArray());
            }
        }
    }
}