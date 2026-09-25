using System;
using SQLite;

namespace NihongoVocab.Models
{
    [Table("WordLists")]
    public class WordList
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [NotNull]
        public string Name { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Ignore]
        public int WordCount { get; set; }

        [Ignore]
        public int UnreviewedCount { get; set; }
    }
}
