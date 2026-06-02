using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Millioner.Models;
using System.Security.Claims;

namespace Millioner.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public ProfileController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(string? userId)
        {
            if (userId == null)
                userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            var model = new ProfileViewModel
            {
                UserName = user.UserName,
                TotalScore = user.TotalScore,
                AvatarPath = user.AvatarPath,
                RegistrationDate = user.RegistrationDate,
                GamesPlayed = user.GamesPlayed,
                GamesWon = user.GamesWon
            };
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> GetUserInfo(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();
            return Json(new
            {
                userName = user.UserName,
                avatarPath = user.AvatarPath,
                totalScore = user.TotalScore,
                gamesPlayed = user.GamesPlayed,
                gamesWon = user.GamesWon
            });
        }
    }
}