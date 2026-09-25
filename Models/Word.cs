using System;
using NihongoVocab.Services;
using SQLite;

namespace NihongoVocab.Models
{
    public enum WordLearningState
    {
        New = 0,
        Learning = 1,
        Review = 2,
        Mastered = 3
    }

    public enum FsrsScheduleState
    {
        New = 0,
        Learning = 1,
        Review = 2,
        Relearning = 3
    }

    [Table("Words")]
    public class Word : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public Word()
        {
            LocalizationService.Instance.LanguageChanged += (s, e) => OnPropertyChanged(string.Empty);
        }

        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed, NotNull]
        public string Text { get; set; } = string.Empty;

        [Ignore]
        public string Kanji => Text;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Indexed]
        public int? WordListId { get; set; }

        public bool IsInList { get; set; }

        public int State { get; set; } = (int)WordLearningState.New;

        public double Stability { get; set; } = 0.0;

        public double Difficulty { get; set; } = 0.0;

        public int Reps { get; set; } = 0;

        public int Lapses { get; set; } = 0;

        public DateTime? LastReviewDate { get; set; }

        public DateTime? NextReviewDate { get; set; }

        [Ignore]
        public string? WordListName { get; set; }

        [Ignore]
        public WordLearningState LearningState => (WordLearningState)State;

        [Ignore]
        public FsrsScheduleState FsrsState
        {
            get
            {
                if (State == (int)WordLearningState.New || Reps == 0)
                {
                    return FsrsScheduleState.New;
                }
                if (State == (int)WordLearningState.Review)
                {
                    return FsrsScheduleState.Review;
                }
                // 处于短周期阶梯（State == Learning）中：
                // 如果经历过遗忘（Lapses > 0），则严格判定为 FSRS 调度算法定义的“重新学习 (Relearning)”状态
                // 若从未遗忘（Lapses == 0），则为首轮初次学习阶梯中的“初学中 (Learning)”状态
                if (Lapses > 0)
                {
                    return FsrsScheduleState.Relearning;
                }
                return FsrsScheduleState.Learning;
            }
        }

        [Ignore]
        public double CurrentRetrievability => Stability <= 0.0 ? 0.0 : Services.FsrsEngine.CalculateRetrievability(Stability, LastReviewDate ?? CreatedAt, DateTime.Now);

        [Ignore]
        public string LearningStateText
        {
            get
            {
                var loc = Services.LocalizationService.Instance;
                return LearningState switch
                {
                    WordLearningState.New => loc.GetString("StateNew", "未学习"),
                    WordLearningState.Learning => loc.GetString("StateLearning", "初学中"),
                    WordLearningState.Review => loc.GetString("StateReview", "复习中"),
                    WordLearningState.Mastered => loc.GetString("StateMastered", "已掌握"),
                    _ => LearningState.ToString()
                };
            }
        }

        [Ignore]
        public string CurveItemToolTip => Services.LocalizationService.Instance.GetString("TooltipWordCurveItem", "左键点击查看个体学习记录与记忆曲线，中键点击复制");

        [Ignore]
        public string NextReviewIntervalText
        {
            get
            {
                var loc = Services.LocalizationService.Instance;
                if (State == (int)WordLearningState.Mastered)
                {
                    return loc.GetString("IntervalMastered", "已掌握 (免复习)");
                }

                if (State == (int)WordLearningState.New || !NextReviewDate.HasValue)
                {
                    return loc.GetString("IntervalUnscheduled", "未安排 (待学习)");
                }

                var diff = NextReviewDate.Value - DateTime.Now;
                if (diff.TotalSeconds <= 0)
                {
                    return loc.GetString("IntervalDue", "已到期 (随时可复习)");
                }

                if (diff.TotalMinutes < 60)
                {
                    int mins = Math.Max(1, (int)diff.TotalMinutes);
                    return string.Format(loc.GetString("IntervalMinutes", "{0} 分钟后"), mins);
                }

                if (diff.TotalHours < 24)
                {
                    int hours = (int)diff.TotalHours;
                    return string.Format(loc.GetString("IntervalHours", "{0} 小时后"), hours);
                }

                if (diff.TotalDays < 30)
                {
                    int days = (int)Math.Ceiling(diff.TotalDays);
                    return string.Format(loc.GetString("IntervalDays", "{0} 天后"), days);
                }

                if (diff.TotalDays < 365)
                {
                    int months = (int)(diff.TotalDays / 30.0);
                    return string.Format(loc.GetString("IntervalMonths", "{0} 个月后"), months);
                }

                return string.Format(loc.GetString("IntervalYears", "{0:F1} 年后"), diff.TotalDays / 365.0);
            }
        }

        [Ignore]
        public string ReviewCountText => string.Format(LocalizationService.Instance.GetString("ReviewCountFormat", "已复习 {0} 次"), Reps);

        [Ignore]
        public string NextReviewLabel => LocalizationService.Instance.GetString("NextReviewLabel", "下次复习");
    }
}
