using System;
using NihongoVocab.Models;

namespace NihongoVocab.Services
{
    public static class FsrsEngine
    {
        // FSRS-4.5 官方标准默认权重矩阵 (19个参数)
        private static readonly double[] W =
        {
            0.40255, 1.18385, 3.173, 15.69105, 
            7.1949, 0.5345, 1.4604, 0.0046, 
            1.54575, 0.1192, 1.01925, 1.9395, 
            0.11, 0.29605, 0.22695, 0.5698, 
            2.8552, 0.52355, 0.5794
        };

        private const double Decay = -0.5;
        private const double Factor = 19.0 / 81.0;
        public const double DefaultDesiredRetention = 0.90;

        /// <summary>
        /// 计算指定经过时间与稳定性下的记忆留存概率 Retrievability R(t, S)
        /// </summary>
        public static double CalculateRetrievability(double stability, DateTime lastReview, DateTime now)
        {
            if (stability <= 0.0001) return 0.0;
            double elapsedDays = Math.Max(0.0, (now - lastReview).TotalDays);
            return Math.Pow(1.0 + Factor * (elapsedDays / stability), Decay);
        }

        /// <summary>
        /// 根据稳定性与期望保留率计算下一次推荐复习间隔（天数）
        /// </summary>
        public static int NextIntervalDays(double stability, double desiredRetention = DefaultDesiredRetention)
        {
            if (stability <= 0.0001) return 1;
            double interval = (stability / Factor) * (Math.Pow(desiredRetention, 1.0 / Decay) - 1.0);
            return Math.Max(1, (int)Math.Round(interval));
        }

        /// <summary>
        /// 执行 FSRS 算法迭代：更新 Stability, Difficulty, Reps, Lapses, NextReviewDate
        /// </summary>
        /// <param name="word">当前单词</param>
        /// <param name="rating">评级 (1=Again 遗忘, 3=Good 记得)</param>
        /// <param name="now">当前时间</param>
        /// <returns>更新后的 (newStability, newDifficulty, nextReviewDate)</returns>
        public static (double NewStability, double NewDifficulty, DateTime NextReviewDate) Review(Word word, int rating, DateTime now)
        {
            double s = word.Stability;
            double d = word.Difficulty;
            int g = rating; // 1 or 3

            double newS;
            double newD;

            if (word.Reps == 0 || s <= 0.0001)
            {
                // 初次学习 (First Review)
                newS = InitStability(g);
                newD = InitDifficulty(g);
            }
            else
            {
                // 复习阶段 (Subsequent Review)
                double elapsedDays = Math.Max(0.0, (now - (word.LastReviewDate ?? word.CreatedAt)).TotalDays);
                double retrievability = Math.Pow(1.0 + Factor * (elapsedDays / s), Decay);

                newD = NextDifficulty(d, g);

                if (g == 1) // Again (遗忘)
                {
                    newS = NextForgetStability(d, s, retrievability);
                }
                else // Good (记得)
                {
                    newS = NextRecallStability(d, s, retrievability, g);
                }
            }

            // 限制取值范围
            newS = Math.Max(0.1, newS);
            newD = Math.Clamp(newD, 1.0, 10.0);

            int intervalDays;
            if (g == 1)
            {
                // 遗忘时重新从当前/明天开始
                intervalDays = 1;
            }
            else
            {
                intervalDays = NextIntervalDays(newS, DefaultDesiredRetention);
            }

            DateTime nextReview = now.AddDays(intervalDays);

            return (newS, newD, nextReview);
        }

        private static double InitStability(int g)
        {
            int index = Math.Clamp(g - 1, 0, 3);
            return W[index];
        }

        private static double InitDifficulty(int g)
        {
            double d0 = W[4] - Math.Exp(W[5] * (g - 1)) + 1.0;
            return Math.Clamp(d0, 1.0, 10.0);
        }

        private static double NextDifficulty(double d, int g)
        {
            double d0Good = InitDifficulty(3); // D0(3)
            double nextD = W[7] * d0Good + (1.0 - W[7]) * (d - W[6] * (g - 3));
            return Math.Clamp(nextD, 1.0, 10.0);
        }

        private static double NextRecallStability(double d, double s, double r, int g)
        {
            double hardPenalty = (g == 2) ? W[15] : 1.0;
            double easyBonus = (g == 4) ? W[16] : 1.0;
            double growth = Math.Exp(W[8]) * (11.0 - d) * Math.Pow(s, -W[9]) * (Math.Exp(W[10] * (1.0 - r)) - 1.0) * hardPenalty * easyBonus;
            return s * (1.0 + growth);
        }

        private static double NextForgetStability(double d, double s, double r)
        {
            return W[11] * Math.Pow(d, -W[12]) * (Math.Pow(s + 1.0, W[13]) - 1.0) * Math.Exp(W[14] * (1.0 - r));
        }
    }
}
