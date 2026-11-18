using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MlpLab
{

    class Program
    {
        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            // Одразу навчаємо перцептрон при старті програми (це займає менше 0.2 сек)
            var net = TrainNetwork();
            var crypto = new Crypto();
            var archiver = new Archiver(crypto);

            while (true)
            {
                Console.WriteLine("\n==================================================");
                Console.WriteLine("   РОЗУМНИЙ ЗАХИЩЕНИЙ АРХІВАТОР ФАЙЛІВ (SARC)     ");
                Console.WriteLine("==================================================");
                Console.WriteLine("1. Заархівувати свої файли");
                Console.WriteLine("2. Розархівувати архів (.arc)");
                Console.WriteLine("3. Запустити повний автотест для захисту проєкту");
                Console.WriteLine("4. Згенерувати таблиці звіту (REPORT.md) та лог ГА");
                Console.WriteLine("0. Вихід");
                Console.Write("\nОберіть дію: ");

                string? choice = Console.ReadLine()?.Trim();

                try
                {
                    if (choice == "1")
                    {
                        CompressInteractive(archiver, net);
                    }
                    else if (choice == "2")
                    {
                        ExtractInteractive(archiver);
                    }
                    else if (choice == "3")
                    {
                        RunFullDemo(archiver, net);
                    }
                    else if (choice == "4")
                    {
                        string reportDir = Path.Combine(Directory.GetCurrentDirectory(), "ProjectReport");
                        ReportGenerator.GenerateFullReport(archiver, net, reportDir);
                    }
                    else if (choice == "0")
                    {
                        break;
                    }
                    else
                    {
                        Console.WriteLine("Невідома команда. Введіть 1, 2, 3 або 0.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n[ПОМИЛКА]: {ex.Message}");
                }
            }
        }

        // РЕЖИМ 1: Архівування реальних файлів користувача
        static void CompressInteractive(Archiver archiver, Mlp net)
        {
            Console.WriteLine("\nВведіть шляхи до файлів через крапку з комою (;)");
            Console.WriteLine(@"Наприклад: C:\Users\Nazar\Desktop\doc.txt; C:\Users\Nazar\Desktop\photo.jpg");
            Console.Write("Файли: ");
            string? input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input)) return;

            string[] files = input.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(f => f.Trim().Trim('"')) // прибираємо лапки, якщо перетягнув файл у консоль
                                  .ToArray();

            Console.Write(@"Куди зберегти архів (напр. C:\Users\Nazar\Desktop\my_archive.arc): ");
            string? outPath = Console.ReadLine()?.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(outPath)) return;

            Console.Write("Придумайте пароль для шифрування: ");
            string password = Console.ReadLine() ?? "";

            Console.Write("Максимальний розмір одного тому в байтах (Enter = 1048576, тобто 1 МБ): ");
            string? volInput = Console.ReadLine();
            int maxVol = int.TryParse(volInput, out int v) && v > 200 ? v : 1048576;

            Console.WriteLine("\nАналіз файлів перцептроном та пакування...");
            var volumes = archiver.ArchiveFiles(files, outPath, password, net, maxVolumeBytes: maxVol, blockSize: 4096);

            Console.WriteLine("\n[ГОТОВО] Створено томів: " + volumes.Count);
            foreach (var vol in volumes)
                Console.WriteLine($" -> {vol} ({new FileInfo(vol).Length} байт)");
        }

        // РЕЖИМ 2: Розархівування
        static void ExtractInteractive(Archiver archiver)
        {
            Console.WriteLine("\nВведіть шлях до архіву (якщо томів кілька, вкажіть усі через крапку з комою ;)");
            Console.Write("Архів(и): ");
            string? input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) return;

            string[] volumes = input.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                    .Select(f => f.Trim().Trim('"'))
                                    .ToArray();

            Console.Write(@"Папка, куди розпакувати файли (напр. C:\Users\Nazar\Desktop\Unpacked): ");
            string? outDir = Console.ReadLine()?.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(outDir)) return;

            Console.Write("Введіть пароль: ");
            string password = Console.ReadLine() ?? "";

            archiver.ExtractVolumes(volumes, outDir, password);
            Console.WriteLine($"\n[ГОТОВО] Усі файли успішно перевірено (HMAC + CRC32) та розпаковано в: {outDir}");
        }

        // Навчання мережі
        static Mlp TrainNetwork()
        {
            double[][] trainX =
            {
                new[] { 0.15, 1.00 },
                new[] { 0.55, 0.95 },
                new[] { 0.40, 0.85 },
                new[] { 0.99, 0.10 },
                new[] { 0.96, 0.25 },
                new[] { 0.88, 0.05 }
            };
            double[][] trainT =
            {
                new[] { 1.0 }, new[] { 1.0 }, new[] { 1.0 },
                new[] { 0.0 }, new[] { 0.0 }, new[] { 0.0 }
            };

            var net = new Mlp(new[] { 2, 5, 5, 1 }, seed: 42);
            var rnd = new Random(42);
            int[] order = Enumerable.Range(0, trainX.Length).ToArray();

            for (int epoch = 1; epoch <= 10000; epoch++)
            {
                for (int k = order.Length - 1; k > 0; k--)
                {
                    int r = rnd.Next(k + 1);
                    (order[k], order[r]) = (order[r], order[k]);
                }

                foreach (int idx in order)
                {
                    net.Forward(trainX[idx]);
                    net.Backward(trainT[idx]);
                    net.Update(0.4);
                }
            }
            return net;
        }

        // РЕЖИМ 3: Автотест для демонстрації викладачу
        static void RunFullDemo(Archiver archiver, Mlp net)
        {
            string workDir = Path.Combine(Directory.GetCurrentDirectory(), "TestWorkspace");
            string inputDir = Path.Combine(workDir, "Input");
            string archiveDir = Path.Combine(workDir, "Archives");
            string outputDir = Path.Combine(workDir, "Extracted");

            if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
            Directory.CreateDirectory(inputDir);
            Directory.CreateDirectory(archiveDir);
            Directory.CreateDirectory(outputDir);

            string textFile = Path.Combine(inputDir, "source_code.cs");
            var sb = new StringBuilder();
            for (int i = 0; i < 60; i++)
                sb.AppendLine($"public void ProcessDataBlock_{i % 5}(int index, byte[] buffer) {{ return; }}");
            File.WriteAllText(textFile, sb.ToString(), Encoding.UTF8);

            string noiseFile = Path.Combine(inputDir, "random_noise.bin");
            File.WriteAllBytes(noiseFile, RandomNumberGenerator.GetBytes(2048));

            string[] inputFiles = { textFile, noiseFile };
            string baseArcPath = Path.Combine(archiveDir, "backup.arc");
            string password = "KNU_IPZ_SecurePassword_2026!";

            var volumes = archiver.ArchiveFiles(inputFiles, baseArcPath, password, net, maxVolumeBytes: 3000, blockSize: 1024);
            archiver.ExtractVolumes(volumes.ToArray(), outputDir, password);

            Console.WriteLine($"\n[Тест] Створено томів: {volumes.Count}. Усі файли розпаковано в {outputDir} побайтово точно!");

            byte[] sampleText = File.ReadAllBytes(textFile);
            Console.WriteLine($"[Тест LZ77] Greedy токенів: {Lz77.CompressGreedy(sampleText).Count}, Lazy токенів: {Lz77.CompressLazy(sampleText).Count}");

            double EvalConfig(CompressorConfig cfg) =>
                (Math.Log2(cfg.WindowSize) / 15.0) + (1.0 - Math.Abs(cfg.EntropyThreshold - 0.90)) - (Math.Abs(cfg.MinMatch - 4) * 0.05);

            var (gaCfg, gaFit, _) = GeneticOptimizer.RunGa(EvalConfig, budget: 30, popSize: 10);
            var (_, rndFit) = GeneticOptimizer.RunRandomSearch(EvalConfig, budget: 30);
            Console.WriteLine($"[Тест ГА] ГА fitness = {gaFit:F4} ({gaCfg}) | Random fitness = {rndFit:F4}");
        }
    }
}
