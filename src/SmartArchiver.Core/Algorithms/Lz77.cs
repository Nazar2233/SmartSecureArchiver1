using System;
using System.Collections.Generic;

namespace MlpLab
{
    // Елемент потоку LZ77: або літерал, або посилання (Distance, Length)
    public readonly struct LzToken
    {
        public readonly bool IsMatch;
        public readonly byte Literal;
        public readonly int Distance;
        public readonly int Length;

        public LzToken(byte literal)
        {
            IsMatch = false;
            Literal = literal;
            Distance = 0;
            Length = 0;
        }

        public LzToken(int distance, int length)
        {
            IsMatch = true;
            Literal = 0;
            Distance = distance;
            Length = length;
        }
    }

    public static class Lz77
    {
        public const int WindowSize = 32768; // Макс. відстань назад
        public const int MinMatch = 3;       // Мін. довжина збігу
        public const int MaxMatch = 258;     // Макс. довжина збігу

        // 1. Жадібний розбір (Greedy) через хеш-таблицю
        public static List<LzToken> CompressGreedy(byte[] data)
        {
            var tokens = new List<LzToken>();
            var hashTable = new Dictionary<int, List<int>>();
            int i = 0;

            while (i < data.Length)
            {
                var (bestDist, bestLen) = FindBestMatch(data, i, hashTable);

                if (bestLen >= MinMatch)
                {
                    tokens.Add(new LzToken(bestDist, bestLen));
                    // Додаємо проміжні позиції в хеш-таблицю, щоб не втрачати історію
                    for (int k = 1; k < bestLen; k++)
                        AddToHash(data, i + k, hashTable);
                    i += bestLen;
                }
                else
                {
                    tokens.Add(new LzToken(data[i]));
                    i++;
                }
            }
            return tokens;
        }

        // 2. Лінивий розбір (Lazy) — заглядає на 1 крок вперед
        public static List<LzToken> CompressLazy(byte[] data)
        {
            var tokens = new List<LzToken>();
            var hashTable = new Dictionary<int, List<int>>();
            int i = 0;

            while (i < data.Length)
            {
                var (curDist, curLen) = FindBestMatch(data, i, hashTable);

                if (curLen >= MinMatch)
                {
                    // Перевіряємо наступну позицію i + 1 (без додавання в хеш раніше часу)
                    var (nextDist, nextLen) = PeekBestMatch(data, i + 1, hashTable);

                    if (nextLen > curLen)
                    {
                        // Відкладаємо поточний збіг і видаємо літерал
                        tokens.Add(new LzToken(data[i]));
                        i++;
                    }
                    else
                    {
                        tokens.Add(new LzToken(curDist, curLen));
                        for (int k = 1; k < curLen; k++)
                            AddToHash(data, i + k, hashTable);
                        i += curLen;
                    }
                }
                else
                {
                    tokens.Add(new LzToken(data[i]));
                    i++;
                }
            }
            return tokens;
        }

        // 3. Декодер LZ77 (строго побайтове копіювання для підтримки перекриттів)
        public static byte[] Decompress(List<LzToken> tokens)
        {
            var output = new List<byte>();

            foreach (var token in tokens)
            {
                if (!token.IsMatch)
                {
                    output.Add(token.Literal);
                }
                else
                {
                    if (token.Distance <= 0 || token.Distance > WindowSize || token.Distance > output.Count)
                        throw new InvalidDataException($"Некоректна відстань LZ77: {token.Distance}");

                    int startPos = output.Count - token.Distance;
                    // Побайтово, бо Distance може бути меншим за Length (наприклад, AAAAAAAAAAAA)
                    for (int k = 0; k < token.Length; k++)
                    {
                        output.Add(output[startPos + k]);
                    }
                }
            }
            return output.ToArray();
        }

        // Пошук найкращого збігу + оновлення хеш-таблиці для позиції pos
        private static (int dist, int len) FindBestMatch(byte[] data, int pos, Dictionary<int, List<int>> table)
        {
            var res = PeekBestMatch(data, pos, table);
            AddToHash(data, pos, table);
            return res;
        }

        // Тільки пошук кандидатів у хеш-таблиці без її зміни
        private static (int dist, int len) PeekBestMatch(byte[] data, int pos, Dictionary<int, List<int>> table)
        {
            if (pos + MinMatch > data.Length) return (0, 0);

            int key = (data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2];
            if (!table.TryGetValue(key, out var candidates)) return (0, 0);

            int bestLen = 0;
            int bestDist = 0;
            int maxPossibleLen = Math.Min(MaxMatch, data.Length - pos);

            // Перебираємо з кінця списку (найближчі збіги — найменша відстань)
            for (int c = candidates.Count - 1; c >= 0; c--)
            {
                int prevPos = candidates[c];
                int dist = pos - prevPos;
                if (dist > WindowSize) break; // Далі вікна 32768 не дивимось

                int len = 0;
                while (len < maxPossibleLen && data[prevPos + len] == data[pos + len])
                    len++;

                if (len >= MinMatch && len > bestLen)
                {
                    bestLen = len;
                    bestDist = dist;
                    if (bestLen == maxPossibleLen) break;
                }
            }
            return (bestDist, bestLen);
        }

        private static void AddToHash(byte[] data, int pos, Dictionary<int, List<int>> table)
        {
            if (pos + MinMatch > data.Length) return;
            int key = (data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2];
            if (!table.TryGetValue(key, out var list))
            {
                list = new List<int>();
                table[key] = list;
            }
            list.Add(pos);
        }
    }
}