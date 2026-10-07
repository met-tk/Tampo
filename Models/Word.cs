using System;
using System.Text.Json.Serialization;
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
        }

        public void NotifyLanguageChanged() => OnPropertyChanged(string.Empty);

        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed, NotNull]
        public string Text { get; set; } = string.Empty;

        [Ignore, JsonIgnore]
        public string Kanji => Text;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime StateUpdatedAt { get; set; } = DateTime.Now;
        public DateTime MetaUpdatedAt { get; set; } = DateTime.Now;

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

        [Ignore, JsonIgnore]
        public WordLearningState LearningState => (WordLearningState)State;

        [Ignore, JsonIgnore]
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
                // 仅当曾经毕业进入过正式复习期（Reps >= 2 且经历过 Lapses）跌落时，才严格判定为 FSRS 算法定义的“重新学习 (Relearning)”状态；
                // 首轮初学阶段（Reps <= 1）的初次遗忘，仍严格归入“初学中 (Learning)”状态
                if (Lapses > 0 && Reps >= 2)
                {
                    return FsrsScheduleState.Relearning;
                }
                return FsrsScheduleState.Learning;
            }
        }

        [Ignore, JsonIgnore]
        public bool IsDue
        {
            get
            {
                if (State == (int)WordLearningState.Mastered) return false;
                DateTime today = DateTime.Today;
                DateTime endOfToday = today.AddDays(1).AddTicks(-1);
                if (LastReviewDate.HasValue && LastReviewDate.Value.Date >= today) return false;
                if (State == (int)WordLearningState.New || Reps <= 0 || !NextReviewDate.HasValue) return true;
                return NextReviewDate.Value <= endOfToday;
            }
        }

        [Ignore, JsonIgnore]
        public double CurrentRetrievability => State == (int)WordLearningState.Mastered
            ? 1.0
            : ((State == (int)WordLearningState.New || Reps <= 0 || Stability <= 0.0)
                ? 0.0
                : Services.FsrsEngine.CalculateRetrievability(Stability, LastReviewDate ?? CreatedAt, DateTime.Now));

        [Ignore, JsonIgnore]
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

        [Ignore, JsonIgnore]
        public string CurveItemToolTip => Services.LocalizationService.Instance.GetString("TooltipWordCurveItem", "左键点击查看个体学习记录与记忆曲线，中键点击复制");

        [Ignore, JsonIgnore]
        public string NextReviewIntervalText
        {
            get
            {
                var loc = Services.LocalizationService.Instance;
                if (State == (int)WordLearningState.Mastered)
                {
                    return loc.GetString("IntervalMastered", "已掌握 (免复习)");
                }

                if (State == (int)WordLearningState.New || Reps <= 0 || !NextReviewDate.HasValue)
                {
                    return loc.GetString("IntervalUnscheduled", "未安排 (待学习)");
                }

                DateTime nextDay = (NextReviewDate.Value.Kind == DateTimeKind.Utc
                    ? NextReviewDate.Value.ToLocalTime()
                    : NextReviewDate.Value).Date;
                int dayDiff = (nextDay - DateTime.Today).Days;

                if (dayDiff <= 0)
                {
                    return loc.GetString("IntervalDue", "已到期 (今日待复习)");
                }

                if (dayDiff < 30)
                {
                    return string.Format(loc.GetString("IntervalDays", "{0} 天后"), dayDiff);
                }

                if (dayDiff < 365)
                {
                    int months = Math.Max(1, dayDiff / 30);
                    return string.Format(loc.GetString("IntervalMonths", "{0} 个月后"), months);
                }

                return string.Format(loc.GetString("IntervalYears", "{0:F1} 年后"), dayDiff / 365.0);
            }
        }

        [Ignore, JsonIgnore]
        public string ReviewCountText => string.Format(LocalizationService.Instance.GetString("ReviewCountFormat", "已复习 {0} 次"), Reps);

        [Ignore, JsonIgnore]
        public string NextReviewLabel => LocalizationService.Instance.GetString("NextReviewLabel", "下次复习");
    }
}
