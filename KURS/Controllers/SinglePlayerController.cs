using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millioner.Data;
using Millioner.Models;
using System.Security.Claims;
using Millioner.Extensions;

namespace Millioner.Controllers
{
    [Authorize]
    public class SinglePlayerController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        private static readonly int[] PrizeTable = new int[]
        {
           100, 200, 300, 500, 1000,
            2000, 4000, 8000, 16000, 32000,
            64000, 125000, 250000, 500000, 1000000
        };

        public SinglePlayerController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var easy = await _context.Questions.Where(q => q.Difficulty == 1).OrderBy(_ => Guid.NewGuid()).Take(5).ToListAsync();
            var medium = await _context.Questions.Where(q => q.Difficulty == 2).OrderBy(_ => Guid.NewGuid()).Take(5).ToListAsync();
            var hard = await _context.Questions.Where(q => q.Difficulty == 3).OrderBy(_ => Guid.NewGuid()).Take(5).ToListAsync();

            if (easy.Count < 5 || medium.Count < 5 || hard.Count < 5)
                return Content("Недостаточно вопросов в базе. Добавьте хотя бы 5 вопросов каждого уровня сложности.");

            var questions = easy.Concat(medium).Concat(hard).ToList();

            HttpContext.Session.SetObjectAsJson("single_questions", questions);
            HttpContext.Session.SetInt32("single_current_index", 0);
            HttpContext.Session.SetInt32("single_total_earned", 0);
            HttpContext.Session.SetInt32("single_last_safe_earned", 0);
            HttpContext.Session.SetInt32("single_answered", 0);

            return RedirectToAction("Play");
        }

        public IActionResult Play()
        {
            var questions = HttpContext.Session.GetObjectFromJson<List<Question>>("single_questions");
            if (questions == null)
                return RedirectToAction("Index");

            int currentIndex = HttpContext.Session.GetInt32("single_current_index") ?? 0;
            if (currentIndex >= questions.Count)
                return RedirectToAction("GameOver", new { win = true });

            var question = questions[currentIndex];
            int currentPrize = PrizeTable[currentIndex];
            int totalEarned = HttpContext.Session.GetInt32("single_total_earned") ?? 0;
            int answered = HttpContext.Session.GetInt32("single_answered") ?? 0;

            bool isSafe = false;
            int safeAmount = 0;
            if (answered >= 11)
            {
                isSafe = true;
                safeAmount = 5000;
            }
            else if (answered >= 6)
            {
                isSafe = true;
                safeAmount = 1000;
            }

            ViewBag.Question = question;
            ViewBag.QuestionNumber = currentIndex + 1;
            ViewBag.TotalQuestions = questions.Count;
            ViewBag.CurrentPrize = currentPrize;
            ViewBag.TotalEarned = totalEarned;
            ViewBag.HasSafe = isSafe;
            ViewBag.SafeAmount = safeAmount;

            return View();
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Answer([FromBody] AnswerDto answerData)
        {
            var questions = HttpContext.Session.GetObjectFromJson<List<Question>>("single_questions");
            if (questions == null) return Json(new { redirect = true, url = Url.Action("Index") });

            int currentIndex = HttpContext.Session.GetInt32("single_current_index") ?? 0;
            if (currentIndex >= questions.Count) return Json(new { redirect = true, url = Url.Action("GameOver", new { win = true }) });

            var q = questions[currentIndex];
            if (q.Id != answerData.questionId) return Json(new { error = "Invalid question" });

            bool isCorrect = (!answerData.timeout) && !string.IsNullOrEmpty(answerData.selectedAnswer) && answerData.selectedAnswer[0] == q.CorrectAnswer;
            char selectedAnswer = isCorrect ? answerData.selectedAnswer[0] : (answerData.selectedAnswer?.FirstOrDefault() ?? '\0');
            int earnedForThisQuestion = PrizeTable[currentIndex];
            int totalEarned = HttpContext.Session.GetInt32("single_total_earned") ?? 0;
            int answered = HttpContext.Session.GetInt32("single_answered") ?? 0;

            // --- Неправильный ответ или таймаут ---
            if (!isCorrect)
            {
                int lastSafe = HttpContext.Session.GetInt32("single_last_safe_earned") ?? 0;
                totalEarned = lastSafe;
                await SaveGameSession(totalEarned, answered);
                var user = await _userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
                if (user != null)
                {
                    user.TotalScore += totalEarned;
                    user.GamesPlayed++;
                    await _userManager.UpdateAsync(user);
                }
                return Json(new
                {
                    correct = false,
                    gameOver = true,
                    win = false,
                    earnedInGame = totalEarned,
                    totalUserScore = user?.TotalScore ?? 0,
                    correctAnswer = q.CorrectAnswer.ToString(),
                    selectedAnswer = selectedAnswer.ToString()
                });
            }

            // --- Правильный ответ ---
            totalEarned += earnedForThisQuestion;
            answered++;
            HttpContext.Session.SetInt32("single_total_earned", totalEarned);
            HttpContext.Session.SetInt32("single_answered", answered);
            HttpContext.Session.SetInt32("single_current_index", currentIndex + 1);

            if (answered == 6) HttpContext.Session.SetInt32("single_last_safe_earned", 1000);
            if (answered == 11) HttpContext.Session.SetInt32("single_last_safe_earned", 5000);

            var currentUser = await _userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));

            // --- Проверка на победу (последний вопрос) ---
            if (currentIndex + 1 >= questions.Count)
            {
                await SaveGameSession(totalEarned, answered);
                if (currentUser != null)
                {
                    currentUser.TotalScore += totalEarned;
                    currentUser.GamesPlayed++;
                    currentUser.GamesWon++;
                    await _userManager.UpdateAsync(currentUser);
                }
                return Json(new
                {
                    correct = true,
                    gameOver = true,
                    win = true,
                    earnedInGame = totalEarned,
                    totalUserScore = currentUser?.TotalScore ?? 0,
                    correctAnswer = q.CorrectAnswer.ToString(),
                    selectedAnswer = selectedAnswer.ToString()
                });
            }

            // --- Игра продолжается ---
            return Json(new
            {
                correct = true,
                earned = earnedForThisQuestion,
                totalEarned = totalEarned,
                gameOver = false,
                correctAnswer = q.CorrectAnswer.ToString(),
                selectedAnswer = selectedAnswer.ToString()
            });
        }

        private async Task SaveGameSession(int totalScore, int correctAnswers)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var session = new GameSession
            {
                UserId = userId,
                ScoreEarned = totalScore,
                QuestionsAnswered = correctAnswers,
                PlayedAt = DateTime.UtcNow
            };
            _context.GameSessions.Add(session);
            await _context.SaveChangesAsync();
        }

        public async Task<IActionResult> GameOver(bool win, int earned = 0)
        {
            if (earned == 0)
                earned = HttpContext.Session.GetInt32("single_total_earned") ?? 0;
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            int totalUserScore = user?.TotalScore ?? 0;

            HttpContext.Session.Remove("single_questions");
            HttpContext.Session.Remove("single_current_index");
            HttpContext.Session.Remove("single_total_earned");
            HttpContext.Session.Remove("single_last_safe_earned");
            HttpContext.Session.Remove("single_answered");

            ViewBag.Win = win;
            ViewBag.EarnedInGame = earned;
            ViewBag.TotalUserScore = totalUserScore;
            return View();
        }
    }
    public class AnswerDto
    {
        public int questionId { get; set; }
        public string? selectedAnswer { get; set; }
        public bool timeout { get; set; }
    }
}
