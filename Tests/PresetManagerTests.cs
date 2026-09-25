using System.Linq;
using NihongoVocab.Models;
using NihongoVocab.Services;
using Xunit;

namespace NihongoVocab.Tests
{
    public class PresetManagerTests
    {
        [Fact]
        public void Cleanse_LineSeparated_TrimsAndDeduplicates()
        {
            var manager = new PresetManager();
            var preset = PresetManager.BuiltInPresets[0]; // 纯文本/换行

            string input = @"
  日本語  
英語
  日本語  
フランス語

";
            var result = manager.Cleanse(input, preset);

            Assert.Equal(3, result.Count);
            Assert.Equal("日本語", result[0]);
            Assert.Equal("英語", result[1]);
            Assert.Equal("フランス語", result[2]);
        }

        [Fact]
        public void Cleanse_EbookPreset_FiltersChapterBulletAndSymbols()
        {
            var manager = new PresetManager();
            var preset = PresetManager.BuiltInPresets[1]; // 电子书标记清洗

            string input = @"
▪ 勉強
• 読書
- 単語
* 文法
";
            var result = manager.Cleanse(input, preset);

            Assert.Equal(4, result.Count);
            Assert.Contains("勉強", result);
            Assert.Contains("読書", result);
            Assert.Contains("単語", result);
            Assert.Contains("文法", result);
        }

        [Fact]
        public void Cleanse_JapanesePreset_ExtractsKanaAndKanjiOnly()
        {
            var manager = new PresetManager();
            var preset = PresetManager.BuiltInPresets[2]; // 纯日文/汉字提取

            string input = "【第1章】今日は良い天気ですね！Hello World 123";
            var result = manager.Cleanse(input, preset);

            Assert.Contains("第", result);
            Assert.Contains("章", result);
            Assert.Contains("今日は良い天気ですね", result);
            Assert.DoesNotContain("Hello", result);
            Assert.DoesNotContain("123", result);
        }

        [Fact]
        public void Cleanse_InverseFilter_ExcludesMatchingLinesAndKeepsUnmatched()
        {
            var manager = new PresetManager();
            var preset = new CleansingPreset
            {
                Name = "排除以#或数字开头的行",
                Pattern = @"^\s*(#|\d+)",
                IsInverseFilter = true,
                MatchWholeLine = true
            };

            string input = @"
# 这是注释行，应该被过滤掉
123 纯数字开头，应该被过滤掉
日本語
   桜 (さくら)
# 另一行注释
富士山
";
            var result = manager.Cleanse(input, preset);

            Assert.Equal(3, result.Count);
            Assert.Contains("日本語", result);
            Assert.Contains("桜 (さくら)", result);
            Assert.Contains("富士山", result);
            Assert.DoesNotContain("# 这是注释行，应该被过滤掉", result);
            Assert.DoesNotContain("123 纯数字开头，应该被过滤掉", result);
        }

        [Fact]
        public void Cleanse_OverrideInverseFilter_WorksAsExpected()
        {
            var manager = new PresetManager();
            var preset = new CleansingPreset
            {
                Name = "包含http的行",
                Pattern = @"http[s]?://",
                IsInverseFilter = false,
                MatchWholeLine = true
            };

            string input = @"
https://example.com/test
日本語
http://sample.jp
";
            var result = manager.Cleanse(input, preset, isInverseFilter: true);

            Assert.Single(result);
            Assert.Equal("日本語", result[0]);
        }

        [Fact]
        public void Cleanse_InverseAndMatchWholeLine_RemovesEntireMatchingLine()
        {
            var manager = new PresetManager();
            var preset = new CleansingPreset
            {
                Name = "排除注释或网址行",
                Pattern = @"(#|https?://)",
                IsInverseFilter = true,
                MatchWholeLine = true
            };

            string input = @"
# 注释行，含有关键字应该整行删除
日本語
https://example.com 含有链接应该整行删除
桜
";
            var result = manager.Cleanse(input, preset);

            Assert.Equal(2, result.Count);
            Assert.Equal("日本語", result[0]);
            Assert.Equal("桜", result[1]);
        }

        [Fact]
        public void Cleanse_InverseAndNotMatchWholeLine_StripsMatchedPortionsAndKeepsRemainder()
        {
            var manager = new PresetManager();
            var preset = new CleansingPreset
            {
                Name = "剔除括号和序号",
                Pattern = @"(\(.*?\)|\[.*?\]|^\d+\.\s*)",
                IsInverseFilter = true,
                MatchWholeLine = false
            };

            string input = @"
1. 桜 (さくら)
2. 勉強 [べんきょう]
食べる
";
            var result = manager.Cleanse(input, preset);

            Assert.Equal(3, result.Count);
            Assert.Equal("桜", result[0]);
            Assert.Equal("勉強", result[1]);
            Assert.Equal("食べる", result[2]);
        }

        [Fact]
        public void Cleanse_NormalAndMatchWholeLine_OutputsEntireLineWhenMatched()
        {
            var manager = new PresetManager();
            var preset = new CleansingPreset
            {
                Name = "命中日文平假名即输出整行",
                Pattern = @"[ぁ-ん]",
                IsInverseFilter = false,
                MatchWholeLine = true
            };

            string input = @"
桜 (さくら) - 包含平假名整行输出
Apple - 纯英文不包含平假名跳过
食べる - 包含假名整行输出
";
            var result = manager.Cleanse(input, preset);

            Assert.Equal(2, result.Count);
            Assert.Equal("桜 (さくら) - 包含平假名整行输出", result[0]);
            Assert.Equal("食べる - 包含假名整行输出", result[1]);
        }

        [Fact]
        public void Cleanse_NormalAndNotMatchWholeLine_ExtractsMatchedPortions()
        {
            var manager = new PresetManager();
            var preset = new CleansingPreset
            {
                Name = "仅提取日文汉字",
                Pattern = @"([一-龥]+)",
                CaptureGroupIndex = 1,
                IsInverseFilter = false,
                MatchWholeLine = false
            };

            string input = @"
1. 桜 (さくら)
2. 勉強 [べんきょう]
";
            var result = manager.Cleanse(input, preset);

            Assert.Equal(2, result.Count);
            Assert.Equal("桜", result[0]);
            Assert.Equal("勉強", result[1]);
        }
    }
}
