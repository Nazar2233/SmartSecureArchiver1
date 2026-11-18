using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MlpLab
{
    public static class Huffman
    {
        private class Node
        {
            public int Symbol; // 0..255 для літералів, 256 для маркера Match
            public int Freq;
            public Node? Left;
            public Node? Right;
        }

        // 1. Покрокове стиснення токенів LZ77 у масив байтів
        public static byte[] Compress(List<LzToken> tokens)
        {
            // Перетворюємо токени на потік символів:
            // Якщо літерал -> байт (0..255). Якщо збіг -> маркер 256 + байти відстані і довжини
            var rawSymbols = new List<int>();
            var extraBytes = new List<byte>();

            foreach (var t in tokens)
            {
                if (!t.IsMatch)
                {
                    rawSymbols.Add(t.Literal);
                }
                else
                {
                    rawSymbols.Add(256); // Спецсимвол для Match
                    extraBytes.Add((byte)(t.Distance >> 8));
                    extraBytes.Add((byte)(t.Distance & 0xFF));
                    extraBytes.Add((byte)(t.Length - Lz77.MinMatch));
                }
            }

            // Рахуємо частоти для побудови дерева Гаффмана (алфавіт 0..256)
            int[] freqs = new int[257];
            foreach (int sym in rawSymbols) freqs[sym]++;

            // Отримуємо довжини кодів через бінарну купу (PriorityQueue)
            int[] codeLengths = BuildCodeLengths(freqs);

            // Генеруємо канонічні коди за правилами специфікації
            var canonicalCodes = BuildCanonicalCodes(codeLengths);

            // Записуємо все в бітовий потік
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            // 1. Записуємо таблицю довжин кодів (257 байтів)
            for (int i = 0; i < 257; i++)
                writer.Write((byte)codeLengths[i]);

            // 2. Записуємо кількість токенів та додаткові байти (відстані/довжини)
            writer.Write(rawSymbols.Count);
            writer.Write(extraBytes.Count);
            writer.Write(extraBytes.ToArray());

            // 3. Пакуємо самі символи бітами (BitWriter)
            var bitBuffer = new List<byte>();
            byte currentByte = 0;
            int bitCount = 0;

            foreach (int sym in rawSymbols)
            {
                var (code, len) = canonicalCodes[sym];
                for (int b = len - 1; b >= 0; b--)
                {
                    int bit = (int)((code >> b) & 1);
                    currentByte = (byte)((currentByte << 1) | bit);
                    bitCount++;
                    if (bitCount == 8)
                    {
                        bitBuffer.Add(currentByte);
                        currentByte = 0;
                        bitCount = 0;
                    }
                }
            }
            if (bitCount > 0)
            {
                currentByte <<= (8 - bitCount); // Доповнюємо нулями до цілого байта
                bitBuffer.Add(currentByte);
            }

            writer.Write(bitBuffer.ToArray());
            return ms.ToArray();
        }

        // 2. Розпакування байтів назад у токени LZ77
        public static List<LzToken> Decompress(byte[] compressedData)
        {
            using var ms = new MemoryStream(compressedData);
            using var reader = new BinaryReader(ms);

            // Читаємо довжини кодів
            int[] codeLengths = new int[257];
            for (int i = 0; i < 257; i++)
                codeLengths[i] = reader.ReadByte();

            int tokenCount = reader.ReadInt32();
            int extraCount = reader.ReadInt32();
            byte[] extraBytes = reader.ReadBytes(extraCount);
            byte[] bitStream = reader.ReadBytes((int)(ms.Length - ms.Position));

            // Відновлюємо канонічні коди та будуємо словник декодування (код + довжина -> символ)
            var canonicalCodes = BuildCanonicalCodes(codeLengths);
            var decodeMap = new Dictionary<(uint code, int len), int>();
            for (int i = 0; i < 257; i++)
            {
                if (codeLengths[i] > 0)
                    decodeMap[(canonicalCodes[i].code, codeLengths[i])] = i;
            }

            // Читаємо біт за бітом і відновлюємо токени
            var tokens = new List<LzToken>(tokenCount);
            uint curCode = 0;
            int curLen = 0;
            int extraIdx = 0;

            foreach (byte b in bitStream)
            {
                for (int bitPos = 7; bitPos >= 0; bitPos--)
                {
                    if (tokens.Count == tokenCount) break;

                    uint bit = (uint)((b >> bitPos) & 1);
                    curCode = (curCode << 1) | bit;
                    curLen++;

                    if (decodeMap.TryGetValue((curCode, curLen), out int sym))
                    {
                        if (sym < 256)
                        {
                            tokens.Add(new LzToken((byte)sym));
                        }
                        else
                        {
                            int dist = (extraBytes[extraIdx] << 8) | extraBytes[extraIdx + 1];
                            int len = extraBytes[extraIdx + 2] + Lz77.MinMatch;
                            extraIdx += 3;
                            tokens.Add(new LzToken(dist, len));
                        }
                        curCode = 0;
                        curLen = 0;
                    }
                }
            }
            return tokens;
        }

        // Побудова дерева через чергу з пріоритетами (бінарну купу)
        private static int[] BuildCodeLengths(int[] freqs)
        {
            var pq = new PriorityQueue<Node, int>();
            int activeSymbols = 0;

            for (int i = 0; i < freqs.Length; i++)
            {
                if (freqs[i] > 0)
                {
                    pq.Enqueue(new Node { Symbol = i, Freq = freqs[i] }, freqs[i]);
                    activeSymbols++;
                }
            }

            int[] lengths = new int[freqs.Length];
            if (activeSymbols == 0) return lengths;

            // Кейс 8 з методички: вироджений алфавіт (тільки 1 унікальний символ у файлі)
            if (activeSymbols == 1)
            {
                var single = pq.Dequeue();
                lengths[single.Symbol] = 1;
                return lengths;
            }

            while (pq.Count > 1)
            {
                var left = pq.Dequeue();
                var right = pq.Dequeue();
                var parent = new Node
                {
                    Symbol = -1,
                    Freq = left.Freq + right.Freq,
                    Left = left,
                    Right = right
                };
                pq.Enqueue(parent, parent.Freq);
            }

            void Traverse(Node? node, int depth)
            {
                if (node == null) return;
                if (node.Symbol >= 0) lengths[node.Symbol] = depth;
                Traverse(node.Left, depth + 1);
                Traverse(node.Right, depth + 1);
            }

            Traverse(pq.Dequeue(), 0);
            return lengths;
        }

        // Генерація детермінованих канонічних кодів за довжинами
        private static (uint code, int len)[] BuildCanonicalCodes(int[] lengths)
        {
            var result = new (uint code, int len)[lengths.Length];
            var sorted = Enumerable.Range(0, lengths.Length)
                                   .Where(s => lengths[s] > 0)
                                   .OrderBy(s => lengths[s])
                                   .ThenBy(s => s)
                                   .ToList();

            uint code = 0;
            int prevLen = 0;

            foreach (int sym in sorted)
            {
                int len = lengths[sym];
                code <<= (len - prevLen);
                result[sym] = (code, len);
                code++;
                prevLen = len;
            }
            return result;
        }
    }
}