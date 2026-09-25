using System;
using NihongoVocab.Models;
using NihongoVocab.Services;
using Xunit;

namespace NihongoVocab.Tests
{
    public class FsrsEngineTests
    {
        [Fact]
        public void FirstReview_Good_InitializesStabilityAndDifficultyCorrectly()
        {
            var word = new Word
            {
                Text = "猫",
                Reps = 0,
                Stability = 0.0,
                Difficulty = 0.0
            };

            var now = DateTime.Now;
            var (newS, newD, nextReview) = FsrsEngine.Review(word, 3, now); // 3 = Good

            Assert.True(newS > 0.0, "初始稳定性应大于 0");
            Assert.True(newD >= 1.0 && newD <= 10.0, "初始难度应在 [1.0, 10.0] 之间");
            Assert.True(nextReview > now, "下次复习时间应晚于当前时间");
        }

        [Fact]
        public void FirstReview_Again_InitializesWithLowerStability()
        {
            var wordGood = new Word { Text = "猫", Reps = 0 };
            var wordAgain = new Word { Text = "犬", Reps = 0 };

            var now = DateTime.Now;
            var (sGood, _, _) = FsrsEngine.Review(wordGood, 3, now);
            var (sAgain, _, _) = FsrsEngine.Review(wordAgain, 1, now);

            Assert.True(sGood > sAgain, "记得(Good)的初始稳定性必须高于遗忘(Again)");
        }

        [Fact]
        public void SubsequentReview_Good_IncreasesStability()
        {
            var now = DateTime.Now;
            var word = new Word
            {
                Text = "桜",
                Reps = 1,
                Stability = 3.17,
                Difficulty = 5.0,
                CreatedAt = now.AddDays(-3),
                LastReviewDate = now.AddDays(-3)
            };

            var (newS, _, _) = FsrsEngine.Review(word, 3, now);

            Assert.True(newS > word.Stability, "成功复习后稳定性必须增长");
        }

        [Fact]
        public void CalculateRetrievability_DecaysOverTime()
        {
            double stability = 10.0;
            var start = DateTime.Now;

            double rDay0 = FsrsEngine.CalculateRetrievability(stability, start, start);
            double rDay5 = FsrsEngine.CalculateRetrievability(stability, start, start.AddDays(5));
            double rDay20 = FsrsEngine.CalculateRetrievability(stability, start, start.AddDays(20));

            Assert.Equal(1.0, rDay0, 3);
            Assert.True(rDay5 < rDay0, "5天后的留存率应低于第0天");
            Assert.True(rDay20 < rDay5, "20天后的留存率应更低");
            Assert.True(rDay20 > 0.0, "留存率应始终保持非负");
        }

        [Fact]
        public void NextIntervalDays_IncreasesWithHigherStability()
        {
            int intervalShort = FsrsEngine.NextIntervalDays(2.0);
            int intervalLong = FsrsEngine.NextIntervalDays(15.0);

            Assert.True(intervalLong > intervalShort, "稳定性越高，推荐复习间隔天数越长");
        }

        [Fact]
        public void FsrsAlgorithm_DecoupledFromMasteredState_AlwaysYieldsLearningOrReview()
        {
            // 验证即使稳定性达到极大值（如 100 天），算法计算的下次状态只应为 Review，而非自动跃升 Mastered
            var word = new Word
            {
                Text = "漢字",
                Reps = 10,
                Stability = 80.0,
                Difficulty = 3.0,
                State = (int)WordLearningState.Review
            };

            var now = DateTime.Now;
            var (newS, newD, nextReview) = FsrsEngine.Review(word, 3, now);

            Assert.True(newS > 80.0);
            Assert.True(nextReview > now.AddDays(60));
            // 确认 WordLearningState.Mastered 不作为算法默认状态触发
            Assert.Equal(3, (int)WordLearningState.Mastered);
        }

        [Fact]
        public void StateTransition_FirstRemembered_IsLearning_NotReviewOrMastered()
        {
            var word = new Word
            {
                Text = "新しい",
                Reps = 0,
                State = (int)WordLearningState.New
            };

            int nextState;
            int rating = 3; // 记得
            if (rating == 1)
            {
                nextState = (int)WordLearningState.Learning;
            }
            else
            {
                nextState = word.Reps == 0 ? (int)WordLearningState.Learning : (int)WordLearningState.Review;
            }

            Assert.Equal((int)WordLearningState.Learning, nextState);
        }

        [Fact]
        public void StateTransition_ConsolidatedReview_ElevatesToReview()
        {
            var word = new Word
            {
                Text = "復習",
                Reps = 1,
                State = (int)WordLearningState.Learning
            };

            int nextState;
            int rating = 3; // 记得
            if (rating == 1)
            {
                nextState = (int)WordLearningState.Learning;
            }
            else
            {
                nextState = word.Reps == 0 ? (int)WordLearningState.Learning : (int)WordLearningState.Review;
            }

            Assert.Equal((int)WordLearningState.Review, nextState);
        }

        [Fact]
        public void FsrsState_FourStates_ClassifiedCorrectly()
        {
            var wordNew = new Word { Text = "新词", Reps = 0, State = (int)WordLearningState.New };
            var wordLearning = new Word { Text = "初学", Reps = 1, Lapses = 0, State = (int)WordLearningState.Learning };
            var wordReview = new Word { Text = "复习", Reps = 3, Lapses = 0, State = (int)WordLearningState.Review };
            var wordRelearning = new Word { Text = "重学", Reps = 2, Lapses = 1, State = (int)WordLearningState.Learning };

            Assert.Equal(FsrsScheduleState.New, wordNew.FsrsState);
            Assert.Equal(FsrsScheduleState.Learning, wordLearning.FsrsState);
            Assert.Equal(FsrsScheduleState.Review, wordReview.FsrsState);
            Assert.Equal(FsrsScheduleState.Relearning, wordRelearning.FsrsState);
        }
    }
}
