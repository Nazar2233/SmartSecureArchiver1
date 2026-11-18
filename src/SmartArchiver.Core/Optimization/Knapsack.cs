using System;
using System.Collections.Generic;
using System.Linq;

namespace MlpLab
{
    public class ArchiveBlock
    {
        public string FileName { get; set; } = "";
        public int BlockIndex { get; set; }
        public byte[] CompressedData { get; set; } = Array.Empty<byte>();
        public bool IsCompressed { get; set; }
        public int RawLength { get; set; }

        // Вага блока в томі за методичкою: coded_len + 15 байтів службового запису
        public int Weight => CompressedData.Length + 15;

        // Цінність блока (визначається за типом файлу або пріоритетом)
        public int Value { get; set; }
    }

    public static class Knapsack
    {
        // 1. Точний розв'язок задачі про рюкзак 0/1 через динамічне програмування (O(n * W))
        public static List<ArchiveBlock> SolveDp(List<ArchiveBlock> candidates, int capacityW)
        {
            int n = candidates.Count;
            if (n == 0 || capacityW <= 0) return new List<ArchiveBlock>();

            // DP[i, w] — максимальна цінність із перших i блоків при місткості w
            int[,] dp = new int[n + 1, capacityW + 1];

            for (int i = 1; i <= n; i++)
            {
                int weight = candidates[i - 1].Weight;
                int val = candidates[i - 1].Value;

                for (int w = 0; w <= capacityW; w++)
                {
                    dp[i, w] = dp[i - 1, w]; // Не беремо блок
                    if (weight <= w)
                    {
                        int take = dp[i - 1, w - weight] + val;
                        if (take > dp[i, w])
                            dp[i, w] = take; // Беремо блок, якщо вигідніше
                    }
                }
            }

            // Відновлення розв'язку (прохід від правого нижнього кута таблиці)
            var selected = new List<ArchiveBlock>();
            int curW = capacityW;
            for (int i = n; i >= 1; i--)
            {
                if (dp[i, curW] != dp[i - 1, curW])
                {
                    var block = candidates[i - 1];
                    selected.Add(block);
                    curW -= block.Weight;
                }
            }

            selected.Reverse();
            return selected;
        }

        // 2. Жадібний базовий алгоритм (сортування за v/w) для порівняння у звіті
        public static List<ArchiveBlock> SolveGreedy(List<ArchiveBlock> candidates, int capacityW)
        {
            var sorted = candidates.OrderByDescending(b => (double)b.Value / b.Weight).ToList();
            var selected = new List<ArchiveBlock>();
            int currentWeight = 0;

            foreach (var block in sorted)
            {
                if (currentWeight + block.Weight <= capacityW)
                {
                    selected.Add(block);
                    currentWeight += block.Weight;
                }
            }
            return selected;
        }
    }
}