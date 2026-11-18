using System;
using System.Linq;
using System.Security.Cryptography;

namespace MlpLab
{
    public class Crypto
    {
        public byte[] K_enc { get; private set; } // Ключ для AES
        public byte[] K_mac { get; private set; } // Ключ для HMAC
        public byte[] Salt { get; private set; }  // Сіль (16 байтів)

        // 1. Виведення ключів з пароля (PBKDF2-HMAC-SHA256, 100 000 ітерацій)
        public void DeriveKeys(string password, byte[] salt = null)
        {
            Salt = salt ?? RandomNumberGenerator.GetBytes(16);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, Salt, 100000, HashAlgorithmName.SHA256);
            byte[] keyMaterial = pbkdf2.GetBytes(64); // Нам треба 64 байти

            K_enc = keyMaterial.Take(32).ToArray(); // Перші 32 байти
            K_mac = keyMaterial.Skip(32).Take(32).ToArray(); // Останні 32 байти
        }

        // 2. Шифрування та розшифрування AES-256-CTR
        // (Оскільки CTR симетричний, ця функція працює і туди, і назад)
        public byte[] AesCtrProcess(byte[] data, byte[] nonce)
        {
            if (nonce.Length != 12)
                throw new ArgumentException("Nonce має бути рівно 12 байтів за специфікацією.");

            byte[] result = new byte[data.Length];

            using var aes = Aes.Create();
            aes.Key = K_enc;
            aes.Mode = CipherMode.ECB; // CTR реалізується через ECB шифрування лічильника
            aes.Padding = PaddingMode.None;

            using var encryptor = aes.CreateEncryptor();
            byte[] counterBlock = new byte[16];
            Buffer.BlockCopy(nonce, 0, counterBlock, 0, 12);

            byte[] encryptedCounter = new byte[16];
            uint blockCounter = 1; // За специфікацією лічильник стартує з 1

            for (int i = 0; i < data.Length; i += 16)
            {
                // Записуємо 4 байти лічильника (Big Endian)
                counterBlock[12] = (byte)(blockCounter >> 24);
                counterBlock[13] = (byte)(blockCounter >> 16);
                counterBlock[14] = (byte)(blockCounter >> 8);
                counterBlock[15] = (byte)(blockCounter);

                encryptor.TransformBlock(counterBlock, 0, 16, encryptedCounter, 0);

                int bytesToXor = Math.Min(16, data.Length - i);
                for (int j = 0; j < bytesToXor; j++)
                {
                    result[i + j] = (byte)(data[i + j] ^ encryptedCounter[j]);
                }
                blockCounter++;
            }
            return result;
        }

        // 3. Обчислення HMAC-SHA256 для контролю цілісності
        public byte[] ComputeHmac(byte[] data)
        {
            using var hmac = new HMACSHA256(K_mac);
            return hmac.ComputeHash(data);
        }

        // Допоміжна перевірка HMAC у постійному часі (захист від timing attacks)
        public bool VerifyHmac(byte[] data, byte[] expectedHmac)
        {
            byte[] actualHmac = ComputeHmac(data);
            return CryptographicOperations.FixedTimeEquals(actualHmac, expectedHmac);
        }
    }
}