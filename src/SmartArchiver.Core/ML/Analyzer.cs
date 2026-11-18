using System;

namespace MlpLab
{
    public class Analyzer
    {
        // 1. Витягуємо ознаки з файлу для нейромережі
        public static double[] ExtractFeatures(byte[] data)
        {
            if (data.Length == 0) return new double[] { 0.0, 0.0 };

            int[] counts = new int[256];
            int asciiCount = 0;

            foreach (byte b in data)
            {
                counts[b]++;
                // Друковані ASCII (32-126) + табуляція/переноси (9, 10, 13)
                if ((b >= 32 && b <= 126) || b == 9 || b == 10 || b == 13)
                    asciiCount++;
            }

            double entropy = 0;
            foreach (int count in counts)
            {
                if (count == 0) continue;
                double p = (double)count / data.Length;
                entropy -= p * Math.Log(p, 2);
            }

            // Ентропія байтів максимум 8, тому ділимо на 8, щоб передати в мережу число 0..1
            double normalizedEntropy = entropy / 8.0;
            double asciiRatio = (double)asciiCount / data.Length;

            return new double[] { normalizedEntropy, asciiRatio };
        }

        // 2. Проганяємо файл через навчений перцептрон
        public static bool ShouldCompress(byte[] data, Mlp trainedNet)
        {
            double[] features = ExtractFeatures(data);
            double[] output = trainedNet.Forward(features);

            // Якщо вихід мережі > 0.5, вважаємо, що файл варто стискати
            bool decision = output[0] > 0.5;

            Console.WriteLine($"   -> Ентропія: {features[0]:F2}, ASCII: {features[1]:F2} | Рішення мережі: {(decision ? "СТИСКАТИ" : "НЕ СТИСКАТИ")}");
            return decision;
        }
    }
}