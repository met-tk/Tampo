using System;
using SQLite;

namespace NihongoVocab.Models
{
    [Table("ReviewLogs")]
    public class ReviewLog
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int WordId { get; set; }

        public int Rating { get; set; } // 1=Again, 3=Good

        public DateTime ReviewDate { get; set; } = DateTime.Now;

        public double StabilityBefore { get; set; }

        public double StabilityAfter { get; set; }

        public double DifficultyBefore { get; set; }

        public double DifficultyAfter { get; set; }

        public int State { get; set; }

        public int ElapsedDays { get; set; }

        public int ScheduledDays { get; set; }

        [Ignore]
        public double Stability
        {
            get => StabilityAfter;
            set
            {
                if (StabilityAfter <= 0 && value > 0)
                {
                    StabilityAfter = value;
                }
            }
        }

        [Ignore]
        public double Difficulty
        {
            get => DifficultyAfter;
            set
            {
                if (DifficultyAfter <= 0 && value > 0)
                {
                    DifficultyAfter = value;
                }
            }
        }
    }
}
