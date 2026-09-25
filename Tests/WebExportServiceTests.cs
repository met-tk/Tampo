using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NihongoVocab.Models;
using NihongoVocab.Services;
using Xunit;

namespace NihongoVocab.Tests
{
    public class WebExportServiceTests
    {
        [Fact]
        public async Task ExportLibraryToHtmlAsync_GeneratesValidHtmlWithWords()
        {
            var service = new WebExportService();
            var words = new List<Word>
            {
                new Word { Id = 1, Text = "桜", CreatedAt = new DateTime(2026, 4, 1), State = 3, Stability = 15.2, Reps = 5 },
                new Word { Id = 2, Text = "青空", CreatedAt = new DateTime(2026, 5, 20), State = 1, Stability = 2.4, Reps = 2 }
            };

            string htmlPath = await service.ExportLibraryToHtmlAsync(words);

            Assert.True(File.Exists(htmlPath));
            string html = await File.ReadAllTextAsync(htmlPath);

            Assert.Contains("Tampo · 离线全景词单", html);
            Assert.Contains("桜", html);
            Assert.Contains("青空", html);
            Assert.Contains("Zero-Definition 纯单词模式", html);
            Assert.Contains("searchInput", html);
            Assert.Contains("timeFilter", html);
        }
    }
}
