using NihongoVocab.Models;
using NihongoVocab.Services;
using Xunit;

namespace NihongoVocab.Tests
{
    public class ExportPresetManagerTests
    {
        [Fact]
        public void FormatWordToSourceLine_ReturnsWordText()
        {
            var word = new Word
            {
                Text = "食べる"
            };

            string sourceLine = ExportPresetManager.FormatWordToSourceLine(word);
            Assert.Equal("食べる", sourceLine);
            Assert.Equal("食べる", word.Kanji);
        }

        [Fact]
        public void FormatLine_LineByLinePreset_ExtractsWord()
        {
            var preset = ExportPresetManager.BuiltInPresets[0]; // 纯单词换行
            string source = "日本語";

            string result = ExportPresetManager.FormatLine(source, preset.Pattern, preset.Replacement);
            Assert.Equal("日本語", result);
        }

        [Fact]
        public void FormatLine_NumberedPreset_FormatsWithIndex()
        {
            var preset = ExportPresetManager.BuiltInPresets[1]; // 序号清单列表 (1. 单词)
            string source = "桜";

            string result = ExportPresetManager.FormatLine(source, preset.Pattern, preset.Replacement, 5);
            Assert.Equal("5. 桜", result);
        }

        [Fact]
        public void FormatLine_MarkdownBulletPreset_FormatsList()
        {
            var preset = ExportPresetManager.BuiltInPresets[2]; // Markdown 列表清单 (- 单词)
            string source = "桜";

            string result = ExportPresetManager.FormatLine(source, preset.Pattern, preset.Replacement);
            Assert.Equal("- 桜", result);
        }

        [Fact]
        public void FormatLine_BracketWrapPreset_FormatsBrackets()
        {
            var preset = ExportPresetManager.BuiltInPresets[3]; // 符号包裹格式 (【单词】)
            string source = "学校";

            string result = ExportPresetManager.FormatLine(source, preset.Pattern, preset.Replacement);
            Assert.Equal("【学校】", result);
        }

        [Fact]
        public void FormatLine_SplitBracketPreset_SplitsWordAndReading()
        {
            var preset = ExportPresetManager.BuiltInPresets[5]; // 括号读音拆分 (若含括号)
            string source = "食べる(たべる)";

            string result = ExportPresetManager.FormatLine(source, preset.Pattern, preset.Replacement);
            Assert.Equal("食べる\tたべる", result);
        }

        [Fact]
        public void FormatLine_CustomRegex_SupportsEscapeAndCaptureGroups()
        {
            string pattern = @"^(.*)$";
            string replacement = @"[Word: $1]\nNextLine";
            string source = "勉強";

            string result = ExportPresetManager.FormatLine(source, pattern, replacement);
            Assert.Equal("[Word: 勉強]\nNextLine", result);
        }
    }
}
