namespace Millioner.Models
{
    public class Question
    {
        public int Id { get; set; }
        public string Text { get; set; } = "";
        public string AnswerA { get; set; } = "";
        public string AnswerB { get; set; } = "";
        public string AnswerC { get; set; } = "";
        public string AnswerD { get; set; } = "";
        public char CorrectAnswer { get; set; } // 'A', 'B', 'C' или 'D'
        public int Difficulty { get; set; }     // 1..15 (уровень вопроса)
    }
}
