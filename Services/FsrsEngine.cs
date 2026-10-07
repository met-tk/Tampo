using System;
using NihongoVocab.Models;

namespace NihongoVocab.Services
{
    public static class FsrsEngine
    {
        // FSRS-4.5 官方标准默认权重矩阵 (17个参数)
        public static readonly double[] W =
        {
            0.4872, 1.4003, 3.7145, 13.8206, 
            5.1618, 1.2298, 0.8975, 0.031, 
            1.6474, 0.1367, 1.0461, 2.1072, 
            0.0793, 0.3246, 1.587, 0.2272, 
            2.8755
        };

        private const double Decay = -0.5;
        private const double Factor = 19.0 / 81.0;
        public const double DefaultDesiredRetention = 0.90;

        /// <summary>
        /// 按自然日（Calendar Day）计算两次时间之间的间隔天数
        /// </summary>
        public static double GetCalendarElapsedDays(DateTime lastReview, DateTime now)
        {
            DateTime lastLocal = lastReview.Kind == DateTimeKind.Utc ? lastReview.ToLocalTime() : lastReview;
            DateTime nowLocal = now.Kind == DateTimeKind.Utc ? now.ToLocalTime() : now;
            return Math.Max(0.0, (nowLocal.Date - lastLocal.Date).TotalDays);
        }

        /// <summary>
        /// 计算指定经过时间（按自然日）与稳定性下的记忆留存概率 Retrievability R(t, S)
        /// </summary>
        public static double CalculateRetrievability(double stability, DateTime lastReview, DateTime now)
        {
            if (double.IsNaN(stability) || double.IsInfinity(stability) || stability <= 0.0001) return 0.0;
            double elapsedDays = GetCalendarElapsedDays(lastReview, now);
            return Math.Pow(1.0 + Factor * (elapsedDays / stability), Decay);
        }

        /// <summary>
        /// 根据稳定性与期望保留率计算下一次推荐复习间隔（天数）
        /// </summary>
        public static int NextIntervalDays(double stability, double desiredRetention = DefaultDesiredRetention)
        {
            if (double.IsNaN(stability) || double.IsInfinity(stability) || stability <= 0.0001) return 1;
            double r = Math.Clamp(desiredRetention, 0.70, 0.99);
            double interval = (stability / Factor) * (Math.Pow(r, 1.0 / Decay) - 1.0);
            if (double.IsNaN(interval) || double.IsInfinity(interval) || interval <= 1.0) return 1;
            return Math.Max(1, (int)Math.Round(interval, MidpointRounding.AwayFromZero));
        }

        /// <summary>
        /// 执行 FSRS 算法迭代：更新 Stability, Difficulty, Reps, Lapses, NextReviewDate
        /// </summary>
        /// <param name="word">当前单词</param>
        /// <param name="rating">评级 (1=Again 遗忘, 2=Hard 困难, 3=Good 记得, 4=Easy 简单)</param>
        /// <param name="now">当前时间</param>
        /// <returns>更新后的 (newStability, newDifficulty, nextReviewDate)</returns>
        public static (double NewStability, double NewDifficulty, DateTime NextReviewDate) Review(Word word, int rating, DateTime now)
        {
            double s = (double.IsNaN(word.Stability) || double.IsInfinity(word.Stability)) ? 0.0 : word.Stability;
            double d = (double.IsNaN(word.Difficulty) || double.IsInfinity(word.Difficulty)) ? 0.0 : word.Difficulty;
            int g = Math.Clamp(rating, 1, 4);

            // 脏数据兜底与安全域 Clamp
            if (d <= 0.0001) d = W[4];
            else d = Math.Clamp(d, 1.0, 10.0);

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
                s = Math.Clamp(s, 0.1, 36500.0);
                double elapsedDays = GetCalendarElapsedDays(word.LastReviewDate ?? word.CreatedAt, now);
                double retrievability = Math.Pow(1.0 + Factor * (elapsedDays / s), Decay);

                newD = NextDifficulty(d, g);

                if (g == 1) // Again (遗忘)
                {
                    newS = NextForgetStability(d, s, retrievability);
                }
                else // Good / Hard / Easy (记得)
                {
                    newS = NextRecallStability(d, s, retrievability, g);
                    if (elapsedDays <= 0.0001)
                    {
                        newS = Math.Max(s, newS);
                    }
                }
            }

            // 限制取值范围
            newS = Math.Clamp(newS, 0.1, 36500.0);
            newD = Math.Clamp(newD, 1.0, 10.0);

            int intervalDays = (g == 1) ? 1 : NextIntervalDays(newS, DefaultDesiredRetention);
            DateTime nextReview = now.Date.AddDays(intervalDays);

            return (newS, newD, nextReview);
        }

        public static double InitStability(int g)
        {
            int index = Math.Clamp(g - 1, 0, 3);
            return W[index];
        }

        public static double InitDifficulty(int g)
        {
            return Math.Clamp(W[4] - W[5] * (g - 3), 1.0, 10.0);
        }

        public static double NextDifficulty(double d, int g)
        {
            if (double.IsNaN(d) || double.IsInfinity(d) || d <= 0.0001) d = W[4];
            d = Math.Clamp(d, 1.0, 10.0);
            double d0Target = W[4]; // D0(3) = W[4]
            // 二元评级（1=遗忘 / 3=记得）均衡阻尼修正：
            // 在期望留存率 R*=0.90 下（9次记得 : 1次遗忘），1次遗忘产生 +2*W[6] 难度增幅，
            // 因此每次 Good(3) 赋予 2/9 的负向阻尼步长，使得期望难度增幅 E[ΔD]=0，消除二元模式下的单向难度通胀漂移。
            double deltaG = (g == 3) ? (2.0 / 9.0) : (g - 3.0);
            double nextD = W[7] * d0Target + (1.0 - W[7]) * (d - W[6] * deltaG);
            return Math.Clamp(nextD, 1.0, 10.0);
        }

        public static double NextRecallStability(double d, double s, double r, int g)
        {
            if (double.IsNaN(d) || double.IsInfinity(d) || d <= 0.0001) d = W[4];
            if (double.IsNaN(s) || double.IsInfinity(s) || s <= 0.0001) s = 0.1;
            d = Math.Clamp(d, 1.0, 10.0);
            s = Math.Clamp(s, 0.1, 36500.0);
            double hardPenalty = (g == 2) ? W[15] : 1.0;
            double easyBonus = (g == 4) ? W[16] : 1.0;
            double growth = Math.Exp(W[8]) * (11.0 - d) * Math.Pow(s, -W[9]) * (Math.Exp(W[10] * (1.0 - r)) - 1.0) * hardPenalty * easyBonus;
            return Math.Clamp(s * (1.0 + growth), 0.1, 36500.0);
        }

        public static double NextForgetStability(double d, double s, double r)
        {
            if (double.IsNaN(d) || double.IsInfinity(d) || d <= 0.0001) d = W[4];
            if (double.IsNaN(s) || double.IsInfinity(s) || s <= 0.0001) s = 0.1;
            d = Math.Clamp(d, 1.0, 10.0);
            s = Math.Clamp(s, 0.1, 36500.0);
            double sf = W[11] * Math.Pow(d, -W[12]) * (Math.Pow(s + 1.0, W[13]) - 1.0) * Math.Exp(W[14] * (1.0 - r));
            return Math.Clamp(Math.Min(s, sf), 0.1, 36500.0);
        }
    }
}
