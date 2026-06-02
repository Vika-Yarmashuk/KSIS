using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Millioner.Models;
using Millioner.Services;
using System.Security.Claims;

namespace Millioner.Controllers
{
    [Authorize]
    public class MultiPlayerController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public MultiPlayerController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public IActionResult Index() => View();

        [HttpGet]
        public async Task<IActionResult> CreateRoom()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Challenge();
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();
            var roomCode = WebSocketGameManager.CreateRoom(userId, user.UserName, user.AvatarPath);
            return RedirectToAction("Room", new { roomCode });
        }

        [HttpGet]
        public async Task<IActionResult> JoinRoom(string roomCode)
        {
            if (string.IsNullOrEmpty(roomCode))
            {
                TempData["Error"] = "Код комнаты не указан";
                return RedirectToAction("Index");
            }
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Challenge();
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();
            var success = WebSocketGameManager.JoinRoom(roomCode.Trim().ToUpper(), userId, user.UserName, user.AvatarPath);
            if (!success)
            {
                TempData["Error"] = "Комната не найдена, заполнена или игра уже началась";
                return RedirectToAction("Index");
            }
            return RedirectToAction("Room", new { roomCode = roomCode.Trim().ToUpper() });
        }

        [HttpGet]
        public IActionResult Room(string roomCode)
        {
            if (string.IsNullOrEmpty(roomCode)) return NotFound();
            var room = WebSocketGameManager.GetRoom(roomCode);
            if (room == null) return NotFound();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.IsOwner = (room.OwnerId == userId);
            ViewBag.RoomCode = room.RoomCode;
            return View();
        }
    }
}