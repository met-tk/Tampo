using System;
using NihongoVocab.Models;
using NihongoVocab.Services;
using Xunit;

namespace NihongoVocab.Tests
{
    public class LocalizationTests
    {
        [Fact]
        public void Word_ReviewCountAndNextReview_Localization_Switching()
        {
            var loc = LocalizationService.Instance;
            var word = new Word { Reps = 3 };

            // 1. 中文环境测试
            loc.CurrentLanguage = "zh-CN";
            Assert.Equal("复习: 3 次", word.ReviewCountText);
            Assert.Equal("下次复习", word.NextReviewLabel);

            // 2. 日文环境测试
            loc.CurrentLanguage = "ja-JP";
            Assert.Equal("復習：3 回", word.ReviewCountText);
            Assert.Equal("次回復習：", word.NextReviewLabel);

            // 3. 英文环境测试
            loc.CurrentLanguage = "en-US";
            Assert.Equal("Reviews: 3 times", word.ReviewCountText);
            Assert.Equal("Next Review:", word.NextReviewLabel);

            // 恢复默认中文
            loc.CurrentLanguage = "zh-CN";
        }

        [Theory]
        [InlineData("zh-CN", "单词列表A", "词单：单词列表A")]
        [InlineData("ja-JP", "単語リストA", "単語リスト：単語リストA")]
        [InlineData("en-US", "List A", "Word List: List A")]
        public void CurrentViewTitle_FormatsCorrectly(string lang, string listName, string expected)
        {
            var loc = LocalizationService.Instance;
            loc.CurrentLanguage = lang;
            var actual = string.Format(loc.GetString("CurrentViewTitleListFormat"), listName);
            Assert.Equal(expected, actual);
            loc.CurrentLanguage = "zh-CN";
        }

        [Theory]
        [InlineData("zh-CN", 2026, 89, "2026 年 (89 词)")]
        [InlineData("ja-JP", 2026, 89, "2026 年 (89 語)")]
        [InlineData("en-US", 2026, 89, "2026 (89 words)")]
        public void DateTreeNodeYearFormat_FormatsCorrectly(string lang, int year, int count, string expected)
        {
            var loc = LocalizationService.Instance;
            loc.CurrentLanguage = lang;
            var actual = string.Format(loc.GetString("DateTreeNodeYearFormat"), year, count);
            Assert.Equal(expected, actual);
            loc.CurrentLanguage = "zh-CN";
        }
    }
}
