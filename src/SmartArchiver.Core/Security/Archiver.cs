using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text;

namespace MlpLab
{
    public class Archiver
    {
        private readonly Crypto _crypto;

        public Archiver(Crypto crypto)
        {
            _crypto = crypto;
        }

        // 1. ПОВНИЙ ЦИКЛ АРХІВАЦІЇ (Аналіз -> LZ77+Huffman -> Рюкзак по томах -> AES+HMAC)
        public List<string> ArchiveFiles(
            string[] filePaths,
            string outputBasePath,
            string password,
            Mlp trainedNet,
            int maxVolumeBytes = 4096,
            int blockSize = 1024)
        {
            if (filePaths == null || filePaths.Length == 0)
                throw new ArgumentException("Список файлів порожній.");

            // Один раз генеруємо сіль та ключі для всього архіву (Лекція 8)
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            _crypto.DeriveKeys(password, salt);

            var allCandidateBlocks = new List<ArchiveBlock>();
            var fileMeta = new Dictionary<string, (int origSize, uint crc32, int totalBlocks)>();

            // Етап 1: Аналіз, розбиття на блоки та стиснення кожного файлу
            foreach (var path in filePaths)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Файл не знайдено: {path}");

                string safeName = Path.GetFileName(path);
                ValidateSafeFileName(safeName);

                byte[] rawData = File.ReadAllBytes(path);
                uint crc32 = ComputeCrc32(rawData);

                // Перцептрон вирішує, чи варто стискати файл
                bool shouldCompress = rawData.Length >= Lz77.MinMatch && Analyzer.ShouldCompress(rawData, trainedNet);
                int priorityValue = shouldCompress ? 100 : 50; // Пріоритет для рюкзака

                // Порожній файл обробляємо як 1 порожній блок (Кейс №2)
                int numBlocks = rawData.Length == 0 ? 1 : (int)Math.Ceiling((double)rawData.Length / blockSize);
                fileMeta[safeName] = (rawData.Length, crc32, numBlocks);

                for (int bIdx = 0; bIdx < numBlocks; bIdx++)
                {
                    int offset = bIdx * blockSize;
                    int len = Math.Min(blockSize, rawData.Length - offset);
                    byte[] chunk = new byte[len];
                    if (len > 0) Buffer.BlockCopy(rawData, offset, chunk, 0, len);

                    byte[] finalBlockData = chunk;
                    bool isBlockCompressed = false;

                    // Якщо мережа радить стискати — пробуємо LZ77 + Гаффман
                    if (shouldCompress && len >= Lz77.MinMatch)
                    {
                        var tokens = Lz77.CompressLazy(chunk);
                        byte[] compressed = Huffman.Compress(tokens);

                        // Свідома відмова від стиснення, якщо воно роздуло дані (Кейс №5 і №6)
                        if (compressed.Length < chunk.Length)
                        {
                            finalBlockData = compressed;
                            isBlockCompressed = true;
                        }
                    }

                    allCandidateBlocks.Add(new ArchiveBlock
                    {
                        FileName = safeName,
                        BlockIndex = bIdx,
                        CompressedData = finalBlockData,
                        IsCompressed = isBlockCompressed,
                        RawLength = len,
                        Value = priorityValue
                    });
                }
            }

            // Етап 2: Пакування блоків у томи через точний ДП-рюкзак 0/1
            var volumesBlocks = new List<List<ArchiveBlock>>();
            var remainingBlocks = new List<ArchiveBlock>(allCandidateBlocks);

            while (remainingBlocks.Count > 0)
            {
                // Фіксуємо таблицю всіх файлів-кандидатів до запуску рюкзака (Лекція 6)
                var candidateFiles = remainingBlocks.Select(b => b.FileName).Distinct().ToList();
                int fileTableOverhead = EstimateFileTableSize(candidateFiles);

                // 41 (заголовок) + 32 (HMAC) + 6 (службові лічильники) + таблиця файлів
                int capacityW = maxVolumeBytes - 41 - 32 - 6 - fileTableOverhead;

                if (capacityW <= 0)
                    throw new InvalidOperationException("Розмір тому замалий навіть для службових заголовків.");

                if (remainingBlocks.Any(b => b.Weight > capacityW))
                    throw new InvalidOperationException("Один із блоків перевищує доступну місткість тому W. Зменште blockSize.");

                // Вибираємо оптимальний набір блоків для поточного тому
                var packedForVolume = Knapsack.SolveDp(remainingBlocks, capacityW);
                if (packedForVolume.Count == 0)
                    throw new InvalidOperationException("Не вдалося запакувати жоден блок у том.");

                volumesBlocks.Add(packedForVolume);
                foreach (var packed in packedForVolume)
                    remainingBlocks.Remove(packed);
            }

            // Етап 3: Формування, шифрування (унікальний Nonce!) та підпис HMAC кожного тому
            var createdVolumes = new List<string>();
            ushort totalVolumes = (ushort)volumesBlocks.Count;
            uint baseNonceCounter = 1; // Послідовний лічильник для гарантії унікальності Nonce (Лекція 9)

            for (int vIdx = 0; vIdx < totalVolumes; vIdx++)
            {
                ushort volumeNumber = (ushort)(vIdx + 1);
                var volBlocks = volumesBlocks[vIdx];

                // Унікальний 12-байтовий Nonce для кожного тому
                byte[] nonce = new byte[12];
                BitConverter.GetBytes(baseNonceCounter++).CopyTo(nonce, 0);
                RandomNumberGenerator.GetBytes(8).CopyTo(nonce, 4);

                // Формуємо внутрішній Payload (таблиця метаданих + блоки)
                using var payloadMs = new MemoryStream();
                using (var pw = new BinaryWriter(payloadMs, Encoding.UTF8))
                {
                    var filesInVol = volBlocks.Select(b => b.FileName).Distinct().ToList();
                    pw.Write(filesInVol.Count);
                    foreach (var fName in filesInVol)
                    {
                        var meta = fileMeta[fName];
                        pw.Write(fName);
                        pw.Write(meta.origSize);
                        pw.Write(meta.crc32);
                        pw.Write(meta.totalBlocks);
                    }

                    pw.Write(volBlocks.Count);
                    foreach (var blk in volBlocks)
                    {
                        pw.Write(blk.FileName);
                        pw.Write(blk.BlockIndex);
                        pw.Write(blk.IsCompressed);
                        pw.Write(blk.RawLength);
                        pw.Write(blk.CompressedData.Length);
                        pw.Write(blk.CompressedData);
                    }
                }

                byte[] rawPayload = payloadMs.ToArray();
                byte[] ciphertext = _crypto.AesCtrProcess(rawPayload, nonce);

                // 41-байтовий відкритий транспортний заголовок
                byte[] header = new byte[41];
                using (var hMs = new MemoryStream(header))
                using (var hw = new BinaryWriter(hMs))
                {
                    hw.Write(Encoding.ASCII.GetBytes("SARC")); // 4 байти: MAGIC
                    hw.Write((byte)1);                         // 1 байт: Версія
                    hw.Write(volumeNumber);                    // 1 байт (молодший): номер тому
                    hw.Write(totalVolumes);                    // 1 байт (молодший): всього томів (разом 2 байти)
                    // Коригуємо позицію рівно під 41 байт:
                    hMs.Position = 5;
                    hw.Write((byte)volumeNumber);
                    hw.Write((byte)totalVolumes);
                    hw.Write(salt);                            // 16 байтів (позиції 7..22)
                    hw.Write(nonce);                           // 12 байтів (позиції 23..34)
                    byte[] lenBytes = BitConverter.GetBytes((long)ciphertext.Length);
                    hMs.Write(lenBytes, 0, 6);                 // 6 байтів (позиції 35..40) -> Разом 41 байт!
                }

                // Encrypt-then-MAC: HMAC рахується від (Header + Ciphertext)
                using var volStream = new MemoryStream();
                volStream.Write(header, 0, header.Length);
                volStream.Write(ciphertext, 0, ciphertext.Length);
                byte[] hmac = _crypto.ComputeHmac(volStream.ToArray());

                string volPath = totalVolumes == 1
                    ? outputBasePath
                    : $"{Path.Combine(Path.GetDirectoryName(outputBasePath) ?? "", Path.GetFileNameWithoutExtension(outputBasePath))}.part{volumeNumber}.arc";

                using var fs = new FileStream(volPath, FileMode.Create);
                fs.Write(volStream.ToArray(), 0, (int)volStream.Length);
                fs.Write(hmac, 0, hmac.Length);

                createdVolumes.Add(volPath);
            }

            return createdVolumes;
        }

        // 2. ПОВНИЙ ЦИКЛ РОЗАРХІВАЦІЇ (Валідація -> Перевірка HMAC ДО дешифрування -> Розпакування -> CRC32)
        public void ExtractVolumes(string[] volumePaths, string outputDirectory, string password)
        {
            if (volumePaths == null || volumePaths.Length == 0)
                throw new ArgumentException("Не вказано жодного тому для розпакування.");

            Directory.CreateDirectory(outputDirectory);

            var seenVolumes = new HashSet<byte>();
            byte expectedTotalVolumes = 0;
            byte[]? archiveSalt = null;

            var assembledBlocks = new Dictionary<string, SortedDictionary<int, byte[]>>();
            var expectedMeta = new Dictionary<string, (int origSize, uint crc32, int totalBlocks)>();

            foreach (var volPath in volumePaths)
            {
                if (!File.Exists(volPath))
                    throw new FileNotFoundException($"Том не знайдено: {volPath}");

                byte[] fileBytes = File.ReadAllBytes(volPath);

                // Мінімальний розмір = 41 (заголовок) + 32 (HMAC)
                if (fileBytes.Length < 73)
                    throw new InvalidDataException("E_TRUNCATED: Архів обрізаний або пошкоджений.");

                // 1. Валідація відкритого заголовка ДО розшифрування
                string magic = Encoding.ASCII.GetString(fileBytes, 0, 4);
                if (magic != "SARC")
                    throw new InvalidDataException("E_MAGIC: Невірна сигнатура формату (очікується SARC).");

                byte version = fileBytes[4];
                if (version != 1)
                    throw new InvalidDataException($"E_VERSION: Непідтримувана версія формату: {version}");

                byte volNum = fileBytes[5];
                byte totalVols = fileBytes[6];

                if (expectedTotalVolumes == 0) expectedTotalVolumes = totalVols;
                else if (totalVols != expectedTotalVolumes)
                    throw new InvalidDataException("E_VOLUME_MISMATCH: Томи належать до різних архівів.");

                if (!seenVolumes.Add(volNum))
                    throw new InvalidDataException($"E_DUPLICATE_VOLUME: Виявлено дублікат тому №{volNum}.");

                byte[] salt = new byte[16];
                Buffer.BlockCopy(fileBytes, 7, salt, 0, 16);

                if (archiveSalt == null)
                {
                    archiveSalt = salt;
                    _crypto.DeriveKeys(password, archiveSalt); // KDF виконується лише 1 раз!
                }
                else if (!archiveSalt.SequenceEqual(salt))
                {
                    throw new InvalidDataException("E_SALT_MISMATCH: Сіль тому не збігається з архівом.");
                }

                byte[] nonce = new byte[12];
                Buffer.BlockCopy(fileBytes, 23, nonce, 0, 12);

                byte[] lenBuffer = new byte[8];
                Buffer.BlockCopy(fileBytes, 35, lenBuffer, 0, 6);
                long cipherLen = BitConverter.ToInt64(lenBuffer, 0);

                if (41 + cipherLen + 32 != fileBytes.Length)
                    throw new InvalidDataException("E_LENGTH: Довжина шифротексту не збігається з розміром файлу.");

                // 2. FAIL CLOSED: Перевіряємо HMAC ДО будь-якого розшифрування!
                byte[] signedData = new byte[41 + cipherLen];
                Buffer.BlockCopy(fileBytes, 0, signedData, 0, signedData.Length);

                byte[] expectedHmac = new byte[32];
                Buffer.BlockCopy(fileBytes, signedData.Length, expectedHmac, 0, 32);

                if (!_crypto.VerifyHmac(signedData, expectedHmac))
                    throw new CryptographicException("E_HMAC: Помилка перевірки цілісності або невірний пароль!");

                // 3. Розшифрування (тільки після успішного HMAC)
                byte[] ciphertext = new byte[cipherLen];
                Buffer.BlockCopy(fileBytes, 41, ciphertext, 0, (int)cipherLen);
                byte[] rawPayload = _crypto.AesCtrProcess(ciphertext, nonce);

                // 4. Розбір метаданих та блоків
                using var payloadMs = new MemoryStream(rawPayload);
                using var pr = new BinaryReader(payloadMs, Encoding.UTF8);

                int filesCount = pr.ReadInt32();
                for (int f = 0; f < filesCount; f++)
                {
                    string fName = pr.ReadString();
                    ValidateSafeFileName(fName); // Захист від Path Traversal
                    int origSize = pr.ReadInt32();
                    uint crc32 = pr.ReadUInt32();
                    int totalBlocks = pr.ReadInt32();

                    expectedMeta[fName] = (origSize, crc32, totalBlocks);
                    if (!assembledBlocks.ContainsKey(fName))
                        assembledBlocks[fName] = new SortedDictionary<int, byte[]>();
                }

                int blocksInVol = pr.ReadInt32();
                for (int b = 0; b < blocksInVol; b++)
                {
                    string fName = pr.ReadString();
                    ValidateSafeFileName(fName);
                    int bIdx = pr.ReadInt32();
                    bool isComp = pr.ReadBoolean();
                    int rawLen = pr.ReadInt32();
                    int compLen = pr.ReadInt32();
                    byte[] blockBytes = pr.ReadBytes(compLen);

                    byte[] restoredChunk = isComp
                        ? Lz77.Decompress(Huffman.Decompress(blockBytes))
                        : blockBytes;

                    if (restoredChunk.Length != rawLen)
                        throw new InvalidDataException($"E_PAYLOAD: Розмір відновленого блока {bIdx} файлу {fName} пошкоджено.");

                    assembledBlocks[fName][bIdx] = restoredChunk;
                }
            }

            // Перевірка, чи всі томи на місці (Кейс №12)
            if (seenVolumes.Count != expectedTotalVolumes)
                throw new InvalidDataException($"E_MISSING_VOLUME: Отримано {seenVolumes.Count} томів із {expectedTotalVolumes}.");

            // 5. Атомарне складання файлів та контроль CRC32
            foreach (var kv in expectedMeta)
            {
                string fName = kv.Key;
                var (origSize, expectedCrc, totalBlocks) = kv.Value;

                if (!assembledBlocks.TryGetValue(fName, out var blocksDict) || blocksDict.Count != totalBlocks)
                    throw new InvalidDataException($"E_INCOMPLETE_FILE: Не вистачає блоків для файлу {fName}.");

                using var fileOut = new MemoryStream(origSize);
                for (int i = 0; i < totalBlocks; i++)
                {
                    byte[] chunk = blocksDict[i];
                    fileOut.Write(chunk, 0, chunk.Length);
                }

                byte[] finalBytes = fileOut.ToArray();
                uint actualCrc = ComputeCrc32(finalBytes);

                if (actualCrc != expectedCrc)
                    throw new InvalidDataException($"E_CRC32: Контрольна сума файлу {fName} не збігається!");

                string destPath = Path.Combine(outputDirectory, fName);
                File.WriteAllBytes(destPath, finalBytes);
            }
        }

        // Захист від Path Traversal (Критерій C5: заборонені /, \, :, ..)
        private static void ValidateSafeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                fileName.Contains('/') || fileName.Contains('\\') ||
                fileName.Contains(':') || fileName.Contains('\0') ||
                fileName == "." || fileName == ".." || fileName.Contains(".."))
            {
                throw new InvalidDataException($"E_PAYLOAD: Небезпечне ім'я файлу в архіві: {fileName}");
            }
        }

        private static int EstimateFileTableSize(List<string> fileNames)
        {
            // Приблизний розмір запису метаданих на файл (довжина імені + 12 байтів службових полів)
            return fileNames.Sum(name => Encoding.UTF8.GetByteCount(name) + 14);
        }

        private static uint ComputeCrc32(byte[] data)
        {
            var crc = new System.IO.Hashing.Crc32();
            crc.Append(data);
            return crc.GetCurrentHashAsUInt32();
        }
    }
}