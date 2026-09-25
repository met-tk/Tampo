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
    }
}
