using System;
using System.IO;
using System.Linq;
using System.Text;
using MlpLab;

namespace SmartArchiver.Tests
{
    internal class Program
    {
        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            int passed = 0, total = 0;

            void RunCase(string name, Action test)
            {
                total++;
                try
                {
                    test();
                    passed++;
                    Console.WriteLine($"[PASS] {name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FAIL] {name}: {ex.Message}");
                }
            }

            var net = new Mlp(new[] { 2, 5, 5, 1 }, seed: 42);
            var crypto = new Crypto();
            var archiver = new Archiver(crypto);
            string work = Path.Combine(Path.GetTempPath(), "SarcTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);

            try
            {
                RunCase("Кейс 1: Текстовий файл архівується і відновлюється побайтово", () =>
                {
                    string f = Path.Combine(work, "c1.txt");
                    byte[] orig = Encoding.UTF8.GetBytes("Тестовий рядок для перевірки побайтового відновлення! " + new string('A', 200));
                    File.WriteAllBytes(f, orig);
                    var vols = archiver.ArchiveFiles(new[] { f }, Path.Combine(work, "c1.arc"), "Pass123!", net);
                    string outDir = Path.Combine(work, "out1");
                    archiver.ExtractVolumes(vols.ToArray(), outDir, "Pass123!");
                    if (!orig.SequenceEqual(File.ReadAllBytes(Path.Combine(outDir, "c1.txt")))) throw new Exception("Дані не збігаються");
                });

                RunCase("Кейс 2: Порожній файл (0 Б) і файл з 1 байта обробляються без помилок", () =>
                {
                    string f0 = Path.Combine(work, "empty.bin");
                    string f1 = Path.Combine(work, "one.bin");
                    File.WriteAllBytes(f0, Array.Empty<byte>());
                    File.WriteAllBytes(f1, new byte[] { 0x7F });
                    var vols = archiver.ArchiveFiles(new[] { f0, f1 }, Path.Combine(work, "c2.arc"), "Pass123!", net);
                    string outDir = Path.Combine(work, "out2");
                    archiver.ExtractVolumes(vols.ToArray(), outDir, "Pass123!");
                    if (File.ReadAllBytes(Path.Combine(outDir, "empty.bin")).Length != 0) throw new Exception("Порожній файл пошкоджено");
                    if (!File.ReadAllBytes(Path.Combine(outDir, "one.bin")).SequenceEqual(new byte[] { 0x7F })) throw new Exception("1 байт пошкоджено");
                });

                RunCase("Кейс 4 і 8: Вироджений алфавіт (однакові байти) стискається і відновлюється", () =>
                {
                    string f = Path.Combine(work, "same.dat");
                    byte[] orig = Enumerable.Repeat((byte)'Z', 4096).ToArray();
                    File.WriteAllBytes(f, orig);
                    var vols = archiver.ArchiveFiles(new[] { f }, Path.Combine(work, "c4.arc"), "Pass123!", net);
                    long arcSize = vols.Sum(v => new FileInfo(v).Length);
                    if (arcSize >= orig.Length / 2) throw new Exception($"Недостатнє стиснення: {arcSize} Б");
                    string outDir = Path.Combine(work, "out4");
                    archiver.ExtractVolumes(vols.ToArray(), outDir, "Pass123!");
                    if (!orig.SequenceEqual(File.ReadAllBytes(Path.Combine(outDir, "same.dat")))) throw new Exception("Дані не збігаються");
                });

                RunCase("Кейс 7: Канонічний Гаффман (round-trip)", () =>
                {
                    byte[] src = Encoding.UTF8.GetBytes("abracadabra simsalabim huffman canonical test!");
                    var tokens = Lz77.CompressGreedy(src);
                    byte[] packed = Huffman.Compress(tokens);
                    var unpackedTokens = Huffman.Decompress(packed);
                    byte[] unpacked = Lz77.Decompress(unpackedTokens);
                    if (!src.SequenceEqual(unpacked)) throw new Exception("Помилка декодування Гаффмана");
                });

                RunCase("Кейс 9: Жадібний і лінивий розбір LZ77 працюють коректно", () =>
                {
                    byte[] src = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("abcd_efgh_abcd_efgh_1234_", 20)));
                    var g = Lz77.CompressGreedy(src);
                    var l = Lz77.CompressLazy(src);
                    if (!src.SequenceEqual(Lz77.Decompress(g)) || !src.SequenceEqual(Lz77.Decompress(l)))
                        throw new Exception("Помилка декодування LZ77");
                });

                RunCase("Кейс 10: ДП-рюкзак дає цінність не меншу за жадібний алгоритм", () =>
                {
                    var blocks = new[]
                    {
                        new ArchiveBlock { BlockIndex = 0, CompressedData = new byte[585], Value = 60 },
                        new ArchiveBlock { BlockIndex = 1, CompressedData = new byte[485], Value = 50 },
                        new ArchiveBlock { BlockIndex = 2, CompressedData = new byte[485], Value = 50 }
                    };
                    var dp = Knapsack.SolveDp(blocks.ToList(), 1000);
                    int dpVal = dp.Sum(b => b.Value);
                    if (dpVal < 100) throw new Exception($"Очікувалось 100, отримано {dpVal}");
                });

                RunCase("Кейс 11: Невірний пароль, підміна байта та обрізаний архів відхиляються", () =>
                {
                    string f = Path.Combine(work, "sec.txt");
                    File.WriteAllText(f, "Конфіденційні дані для перевірки Encrypt-then-MAC");
                    var vols = archiver.ArchiveFiles(new[] { f }, Path.Combine(work, "c11.arc"), "RightPass", net);

                    bool wrongPassRejected = false;
                    try { archiver.ExtractVolumes(vols.ToArray(), Path.Combine(work, "bad1"), "WrongPass"); }
                    catch { wrongPassRejected = true; }
                    if (!wrongPassRejected) throw new Exception("Невірний пароль не відхилено");

                    string tampered = Path.Combine(work, "tampered.arc");
                    byte[] raw = File.ReadAllBytes(vols[0]);
                    raw[raw.Length / 2] ^= 0xFF;
                    File.WriteAllBytes(tampered, raw);

                    bool tamperRejected = false;
                    try { archiver.ExtractVolumes(new[] { tampered }, Path.Combine(work, "bad2"), "RightPass"); }
                    catch { tamperRejected = true; }
                    if (!tamperRejected) throw new Exception("Модифікацію шифротексту не виявлено");
                });

                RunCase("Кейс 12: Багатотомний архів відновлюється незалежно від порядку томів", () =>
                {
                    string f = Path.Combine(work, "multi.txt");
                    byte[] orig = Encoding.UTF8.GetBytes(new string('X', 5000) + new string('Y', 5000));
                    File.WriteAllBytes(f, orig);
                    var vols = archiver.ArchiveFiles(new[] { f }, Path.Combine(work, "c12.arc"), "Pass123!", net, maxVolumeBytes: 600, blockSize: 512);
                    if (vols.Count < 2) throw new Exception("Очікувалось декілька томів");
                    string outDir = Path.Combine(work, "out12");
                    archiver.ExtractVolumes(vols.AsEnumerable().Reverse().ToArray(), outDir, "Pass123!");
                    if (!orig.SequenceEqual(File.ReadAllBytes(Path.Combine(outDir, "multi.txt")))) throw new Exception("Помилка збірки томів");
                });
            }
            finally
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
            }

            Console.WriteLine($"\nПідсумок тестів: {passed}/{total} пройдено.");
            return passed == total ? 0 : 1;
        }
    }
}
