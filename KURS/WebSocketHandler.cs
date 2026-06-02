using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Millioner.Data;
using Millioner.Models;

namespace Millioner.Services
{
    public static class WebSocketHandler
    {
        private static readonly Dictionary<string, WebSocketConnection> _connections = new();
        private static readonly object _lock = new();

        public static async Task HandleAsync(WebSocket webSocket, string userId, IServiceProvider serviceProvider)
        {
            var connectionId = Guid.NewGuid().ToString();
            _connections[connectionId] = new WebSocketConnection
            {
                WebSocket = webSocket,
                UserId = userId,
                ServiceProvider = serviceProvider
            };
            var buffer = new byte[4096];
            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                        RemoveConnection(connectionId);
                        break;
                    }
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessMessage(connectionId, message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket error: {ex.Message}");
                RemoveConnection(connectionId);
            }
        }

        private static void RemoveConnection(string connectionId)
        {
            lock (_lock)
            {
                if (_connections.TryGetValue(connectionId, out var conn))
                {
                    WebSocketGameManager.RemovePlayer(conn.UserId);
                    _connections.Remove(connectionId);
                }
            }
        }

        private static async Task ProcessMessage(string connectionId, string json)
        {
            if (!_connections.TryGetValue(connectionId, out var conn)) return;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "createRoom":
                    using (var scope = conn.ServiceProvider.CreateScope())
                    {
                        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                        var user = await userManager.FindByIdAsync(conn.UserId);
                        if (user == null) return;
                        var roomCode = WebSocketGameManager.CreateRoom(conn.UserId, user.UserName, user.AvatarPath);
                        await SendToClient(connectionId, new { type = "roomCreated", roomCode });
                    }
                    break;
                case "joinRoom":
                    var rawCode = root.GetProperty("roomCode").GetString();
                    var code = rawCode?.Trim().ToUpperInvariant();
                    using (var scope = conn.ServiceProvider.CreateScope())
                    {
                        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                        var user = await userManager.FindByIdAsync(conn.UserId);
                        if (user == null) return;
                        var success = WebSocketGameManager.JoinRoom(code, conn.UserId, user.UserName, user.AvatarPath);
                        if (success)
                        {
                            await SendToClient(connectionId, new { type = "joinedRoom", roomCode = code });
                            await BroadcastToRoom(code, new { type = "playersUpdate", players = WebSocketGameManager.GetPlayersInRoom(code) });
                        }
                        else
                        {
                            await SendToClient(connectionId, new { type = "error", message = "Комната не найдена или заполнена" });
                        }
                    }
                    break;
                case "startGame":
                    using (var scope = conn.ServiceProvider.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        var started = await WebSocketGameManager.StartGameAsync(conn.UserId, dbContext);
                        if (started)
                        {
                            var room = WebSocketGameManager.GetRoomByOwner(conn.UserId);
                            if (room != null)
                            {
                                await BroadcastToRoom(room.RoomCode, new { type = "gameStarted" });
                                await SendQuestionToRoom(room.RoomCode, room.CurrentQuestionIndex, conn.ServiceProvider);
                            }
                        }
                        else
                        {
                            await SendToClient(connectionId, new { type = "error", message = "Недостаточно игроков или вопросов" });
                        }
                    }
                    break;
                case "answer":
                    var answerStr = root.GetProperty("answer").GetString();
                    char selectedAnswer = string.IsNullOrEmpty(answerStr) ? '\0' : answerStr[0];
                    var questionId = root.GetProperty("questionId").GetInt32();
                    using (var scope = conn.ServiceProvider.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        var result = await WebSocketGameManager.SubmitAnswer(conn.UserId, questionId, selectedAnswer, dbContext);
                        await SendToClient(connectionId, new
                        {
                            type = "answerResult",
                            correct = result.Correct,
                            earned = result.Earned,
                            eliminated = result.Eliminated,
                            safeEarned = result.SafeEarned,
                            correctAnswer = result.CorrectAnswer.ToString(),
                            selectedAnswer = result.SelectedAnswer.ToString()
                        });
                        var room = WebSocketGameManager.GetRoomByPlayer(conn.UserId);
                        if (room != null)
                        {
                            Console.WriteLine($"[WebSocketHandler] RoundFinished, room.IsGameOver={room.IsGameOver}");
                            await BroadcastToRoom(room.RoomCode, new { type = "playersUpdate", players = WebSocketGameManager.GetPlayersInRoom(room.RoomCode) });
                            if (result.RoundFinished)
                            {
                                if (room.IsGameOver)
                                    await FinishGame(room.RoomCode, conn.ServiceProvider);
                                else
                                    await SendQuestionToRoom(room.RoomCode, room.CurrentQuestionIndex, conn.ServiceProvider);
                            }
                        }
                    }
                    break;
            }
        }

        private static async Task SendQuestionToRoom(string roomCode, int questionIndex, IServiceProvider sp)
        {
            var room = WebSocketGameManager.GetRoom(roomCode);
            if (room == null || room.Questions == null || questionIndex >= room.Questions.Count) return;
            var q = room.Questions[questionIndex];
            int price = q.Difficulty == 1 ? 100 : (q.Difficulty == 2 ? 500 : 1000);
            int safeAmount = 0;
            if (questionIndex + 1 >= 11) safeAmount = 5000;
            else if (questionIndex + 1 >= 6) safeAmount = 1000;
            var dto = new
            {
                type = "newQuestion",
                questionId = q.Id,
                text = q.Text,
                answers = new[] { q.AnswerA, q.AnswerB, q.AnswerC, q.AnswerD },
                price,
                safeAmount,
                index = questionIndex + 1,
                total = room.Questions.Count,
                correctAnswer = q.CorrectAnswer.ToString()
            };
            await BroadcastToRoom(roomCode, dto, onlyAlive: true);
        }

        private static async Task FinishGame(string roomCode, IServiceProvider sp)
        {
            using (var scope = sp.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var (winnerName, players) = await WebSocketGameManager.FinishGameAsync(roomCode, dbContext);
                Console.WriteLine($"Sending gameOver to room {roomCode}, winner={winnerName}");
                await BroadcastToRoom(roomCode, new { type = "gameOver", winner = winnerName, players = players });

                WebSocketGameManager.RemoveRoom(roomCode);
            }
        }

        private static async Task SendToClient(string connectionId, object data)
        {
            if (_connections.TryGetValue(connectionId, out var conn) && conn.WebSocket.State == WebSocketState.Open)
            {
                var json = JsonSerializer.Serialize(data);
                var bytes = Encoding.UTF8.GetBytes(json);
                await conn.WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        private static async Task BroadcastToRoom(string roomCode, object data, bool onlyAlive = false)
        {
            var players = WebSocketGameManager.GetPlayersInRoom(roomCode);
            Console.WriteLine($"[BroadcastToRoom] room={roomCode}, onlyAlive={onlyAlive}, players count={players.Count}");
            if (players.Count == 0) { Console.WriteLine("[BroadcastToRoom] No players to broadcast to - skipping"); return; }
            var json = JsonSerializer.Serialize(data);
            Console.WriteLine($"[BroadcastToRoom] message: {json}");
            var bytes = Encoding.UTF8.GetBytes(json);
            foreach (var player in players)
            {
                if (onlyAlive && !player.IsAlive) continue;
                var conn = _connections.Values.FirstOrDefault(c => c.UserId == player.UserId);
                if (conn?.WebSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        await conn.WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                        Console.WriteLine($"[BroadcastToRoom] sent to {player.UserId}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[BroadcastToRoom] Failed to send to {player.UserId}: {ex.Message}");
                    }
                } else Console.WriteLine($"[BroadcastToRoom] no open connection for {player.UserId}");
            }
        }

        private class WebSocketConnection
        {
            public WebSocket WebSocket { get; set; }
            public string UserId { get; set; }
            public IServiceProvider ServiceProvider { get; set; }
        }
    }
}