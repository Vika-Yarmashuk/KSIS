using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millioner.Models;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Millioner.Data;

namespace Millioner.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ApplicationDbContext _context;

        public HomeController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
        }

        // Главная страница: профиль пользователя + таблица лидеров
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return Challenge();

            // Топ-10 игроков по общему счёту
            var leaderboard = await _context.Users
                .OrderByDescending(u => u.TotalScore)
                .Take(10)
                .Select(u => new LeaderboardEntry
                {
                    UserName = u.UserName,
                    AvatarPath = u.AvatarPath,  
                    TotalScore = u.TotalScore
                })
                .ToListAsync();

            var model = new HomeViewModel
            {
                CurrentUser = user,
                Leaderboard = leaderboard
            };

            return View(model);
        }

        // Обновление имени и аватарки (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProfile(string userName, string avatarPath)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return Json(new { success = false, message = "Пользователь не найден" });

            if (!string.IsNullOrEmpty(userName) && userName != user.UserName)
            {
                var existingUser = await _userManager.FindByNameAsync(userName);
                if (existingUser != null && existingUser.Id != userId)
                    return Json(new { success = false, message = "Это имя уже занято" });
                user.UserName = userName;
            }

            // Проверяем, что передан корректный путь к картинке
            if (!string.IsNullOrEmpty(avatarPath) && avatarPath.StartsWith("/images/avatars/"))
                user.AvatarPath = avatarPath;

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
                return Json(new { success = true, userName = user.UserName, avatarPath = user.AvatarPath, totalScore = user.TotalScore });
            else
                return Json(new { success = false, message = "Ошибка обновления" });
        }

        // Выход из аккаунта
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return Redirect("~/Identity/Account/Login");
        }
    }

    
   

   
}