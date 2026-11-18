using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MlpLab
{
    public static class AttackModule
    {
        // Допоміжний метод: імітація слабкого шифрування (XOR із коротким ключем) для демонстрації атаки
        public static byte[] WeakXorEncrypt(byte[] data, byte[] key)
        {
            byte[] res = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                res[i] = (byte)(data[i] ^ key[i % key.Length]);
            return res;
        }

        // 1. Криптоаналіз слабкого XOR-шифру генетичним алгоритмом за частотами n-грам
        public static (byte[] recoveredKey, string decryptedPreview, double fitness) CrackWeakXorWithGa(
            byte[] ciphertext,
            int keyLength,
            int popSize = 80,
            int generations = 150,
            int seed = 42)
        {
            var rnd = new Random(seed);

            // Початкова популяція випадкових ключів довжини keyLength
            var population = new List<byte[]>();
            for (int i = 0; i < popSize; i++)
            {
                byte[] candidate = new byte[keyLength];
                rnd.NextBytes(candidate);
                population.Add(candidate);
            }

            byte[] bestKey = new byte[keyLength];
            double bestFitness = double.NegativeInfinity;

            for (int gen = 0; gen < generations; gen++)
            {
                var scored = population
                    .Select(k => (key: k, score: EvaluateNgramFitness(WeakXorEncrypt(ciphertext, k))))
                    .OrderByDescending(x => x.score)
                    .ToList();

                if (scored[0].score > bestFitness)
                {
                    bestFitness = scored[0].score;
                    bestKey = (byte[])scored[0].key.Clone();
                }

                // Елітизм: залишаємо топ-10% найкращих ключів
                int eliteCount = Math.Max(2, popSize / 10);
                var nextPop = scored.Take(eliteCount).Select(x => (byte[])x.key.Clone()).ToList();

                // Схрещування та мутація для решти популяції
                while (nextPop.Count < popSize)
                {
                    var p1 = Tournament(scored, 4, rnd);
                    var p2 = Tournament(scored, 4, rnd);
                    byte[] child = new byte[keyLength];

                    int cut = rnd.Next(1, Math.Max(2, keyLength));
                    for (int i = 0; i < keyLength; i++)
                    {
                        child[i] = i < cut ? p1[i] : p2[i];
                        // Мутація окремого байта ключа (ймовірність 20%)
                        if (rnd.NextDouble() < 0.20)
                            child[i] = (byte)rnd.Next(256);
                    }
                    nextPop.Add(child);
                }

                population = nextPop;
            }

            byte[] decrypted = WeakXorEncrypt(ciphertext, bestKey);
            string preview = Encoding.ASCII.GetString(decrypted.Take(Math.Min(80, decrypted.Length)).ToArray());
            return (bestKey, preview, bestFitness);
        }

        // Функція пристосованості (fitness) на основі частот монограм та біграм англійського тексту
        private static double EvaluateNgramFitness(byte[] text)
        {
            if (text.Length == 0) return 0;

            double score = 0;
            // Найчастіші символи та біграми природного тексту / коду
            string commonChars = " etaoinshrdluETAOINSHRDLCU.,\n\r;(){}";
            HashSet<string> commonBigrams = new HashSet<string>
            {
                "th", "he", "in", "er", "an", "re", "on", "at", "en", "nd",
                "ti", "es", "or", "te", "of", "ed", "is", "it", "al", "ar",
                "st", "to", "nt", "ng", "se", "ha", "as", "ou", "io", "le",
                "e ", "t ", "s ", "d ", "n ", "r ", "y ", " t", " a", " i"
            };

            for (int i = 0; i < text.Length; i++)
            {
                byte b = text[i];

                // Штраф за недруковані керівні символи (крім \r, \n, \t)
                if ((b < 32 && b != 9 && b != 10 && b != 13) || b > 126)
                {
                    score -= 10.0;
                    continue;
                }

                char c = (char)b;
                if (commonChars.Contains(c))
                    score += 2.0;
                else
                    score += 0.5;

                // Бонус за пробіли (найчастіший символ у тексті)
                if (b == 32) score += 1.5;

                // Оцінка біграм (2 символи поспіль)
                if (i + 1 < text.Length)
                {
                    char nextC = (char)text[i + 1];
                    string bigram = $"{char.ToLowerInvariant(c)}{char.ToLowerInvariant(nextC)}";
                    if (commonBigrams.Contains(bigram))
                        score += 4.0;
                }
            }

            return score / text.Length;
        }

        private static byte[] Tournament(List<(byte[] key, double score)> scored, int k, Random rnd)
        {
            var best = scored[rnd.Next(scored.Count)];
            for (int i = 1; i < k; i++)
            {
                var contender = scored[rnd.Next(scored.Count)];
                if (contender.score > best.score) best = contender;
            }
            return best.key;
        }

        // 2. Базова перевірка цілісності захищеного архіву (модифікація байта -> обов'язкова відмова)
        public static bool VerifyTamperResistance(Archiver archiver, string validArchivePath, string password)
        {
            string tempTampered = validArchivePath + ".tampered_test";
            string tempOutDir = Path.Combine(Path.GetTempPath(), "sarc_audit_" + Guid.NewGuid().ToString("N"));

            try
            {
                byte[] bytes = File.ReadAllBytes(validArchivePath);
                if (bytes.Length < 80) return false;

                // Змінюємо 1 байт усередині шифротексту
                bytes[45] ^= 0x01;
                File.WriteAllBytes(tempTampered, bytes);

                archiver.ExtractVolumes(new[] { tempTampered }, tempOutDir, password);
                // Якщо виняток не кинуто — захист провалено!
                return false;
            }
            catch
            {
                // Архів успішно відхилено через HMAC!
                return true;
            }
            finally
            {
                if (File.Exists(tempTampered)) File.Delete(tempTampered);
                if (Directory.Exists(tempOutDir)) Directory.Delete(tempOutDir, true);
            }
        }
    }
}