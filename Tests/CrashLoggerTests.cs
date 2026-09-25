using System;
using System.IO;
using NihongoVocab.Services;
using Xunit;

namespace NihongoVocab.Tests
{
    public class CrashLoggerTests
    {
        [Fact]
        public void LogException_WritesFormattedExceptionToFile()
        {
            try
            {
                throw new InvalidOperationException("测试异常触发", new ArgumentNullException("paramName", "内部空引用参数"));
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "UnitTesting");
            }

            string logPath = CrashLogger.GetLogPath();
            Assert.True(File.Exists(logPath));

            string content = File.ReadAllText(logPath);
            Assert.Contains("测试异常触发", content);
            Assert.Contains("InvalidOperationException", content);
            Assert.Contains("ArgumentNullException", content);
            Assert.Contains("UnitTesting", content);
        }

        [Fact]
        public void LogMessage_WritesTimestampedLine()
        {
            string testMsg = $"TestMessage_{Guid.NewGuid()}";
            CrashLogger.LogMessage(testMsg, "WARN");

            string logPath = CrashLogger.GetLogPath();
            Assert.True(File.Exists(logPath));

            string content = File.ReadAllText(logPath);
            Assert.Contains(testMsg, content);
            Assert.Contains("[WARN]", content);
        }
    }
}
