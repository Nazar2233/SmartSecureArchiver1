using System;
using System.Collections.Generic;
using System.Linq;

namespace MlpLab
{
    public class CompressorConfig
    {
        public int WindowSize { get; set; }
        public int MinMatch { get; set; }
        public double EntropyThreshold { get; set; }

        public override string ToString() =>
            $"Window={WindowSize}, MinMatch={MinMatch}, EntropyThresh={EntropyThreshold:F2}";
    }

    public static class GeneticOptimizer
    {
        // Списки допустимих значень для генів хромосоми
        private static readonly int[] WindowOptions = { 1024, 2048, 4096, 8192, 16384, 32768 };
        private static readonly int[] MinMatchOptions = { 3, 4, 5, 6, 8 };
        private static readonly double[] EntropyOptions = { 0.75, 0.85, 0.90, 0.95, 0.98 };

        // Хромосома = [window_idx, min_match_idx, entropy_idx]
        private class Individual
        {
            public int[] Genes = new int[3];
            public double Fitness;

            public Individual Clone() => new Individual
            {
                Genes = (int[])Genes.Clone(),
                Fitness = Fitness
            };
        }

        public static CompressorConfig DecodeChromosome(int[] genes) => new CompressorConfig
        {
            WindowSize = WindowOptions[genes[0]],
            MinMatch = MinMatchOptions[genes[1]],
            EntropyThreshold = EntropyOptions[genes[2]]
        };

        // 1. Основний цикл Генетичного Алгоритму (турнір k=3, одноточковий кросовер, мутація, елітизм)
        public static (CompressorConfig bestConfig, double bestFitness, List<double> history) RunGa(
            Func<CompressorConfig, double> fitnessFunc,
            int budget = 40,
            int popSize = 10,
            int elitismCount = 2,
            double mutationRate = 0.15,
            int seed = 42)
        {
            var rnd = new Random(seed);
            int generations = Math.Max(1, budget / popSize);
            var history = new List<double>();

            // Крок 1: Початкова випадкова популяція
            var population = new List<Individual>();
            for (int i = 0; i < popSize; i++)
                population.Add(CreateRandom(rnd));

            Individual? bestEver = null;

            for (int gen = 0; gen < generations; gen++)
            {
                // Оцінка fitness для кожної хромосоми
                foreach (var ind in population)
                {
                    ind.Fitness = fitnessFunc(DecodeChromosome(ind.Genes));
                    if (bestEver == null || ind.Fitness > bestEver.Fitness)
                        bestEver = ind.Clone();
                }

                // Сортуємо за спаданням пристосованості
                population = population.OrderByDescending(x => x.Fitness).ToList();
                history.Add(bestEver!.Fitness);

                if (gen == generations - 1) break;

                // Крок 5: Елітизм (копіюємо найкращих без змін -> гарантія best_final >= best_initial)
                var nextPop = population.Take(elitismCount).Select(x => x.Clone()).ToList();

                // Добудовуємо решту популяції через селекцію, схрещування та мутацію
                while (nextPop.Count < popSize)
                {
                    var p1 = TournamentSelect(population, 3, rnd);
                    var p2 = TournamentSelect(population, 3, rnd);
                    var child = SinglePointCrossover(p1, p2, rnd);
                    Mutate(child, mutationRate, rnd);
                    nextPop.Add(child);
                }

                population = nextPop;
            }

            return (DecodeChromosome(bestEver!.Genes), bestEver.Fitness, history);
        }

        // 2. Випадковий пошук із ТИМ САМИМ бюджетом обчислень fitness (для чесного порівняння)
        public static (CompressorConfig bestConfig, double bestFitness) RunRandomSearch(
            Func<CompressorConfig, double> fitnessFunc,
            int budget = 40,
            int seed = 42)
        {
            var rnd = new Random(seed);
            Individual? best = null;

            for (int i = 0; i < budget; i++)
            {
                var candidate = CreateRandom(rnd);
                candidate.Fitness = fitnessFunc(DecodeChromosome(candidate.Genes));
                if (best == null || candidate.Fitness > best.Fitness)
                    best = candidate;
            }

            return (DecodeChromosome(best!.Genes), best.Fitness);
        }

        private static Individual CreateRandom(Random rnd) => new Individual
        {
            Genes = new[]
            {
                rnd.Next(WindowOptions.Length),
                rnd.Next(MinMatchOptions.Length),
                rnd.Next(EntropyOptions.Length)
            }
        };

        // Турнірний відбір (k = 3)
        private static Individual TournamentSelect(List<Individual> pop, int k, Random rnd)
        {
            Individual? best = null;
            for (int i = 0; i < k; i++)
            {
                var contender = pop[rnd.Next(pop.Count)];
                if (best == null || contender.Fitness > best.Fitness)
                    best = contender;
            }
            return best!;
        }

        // Одноточкове схрещування
        private static Individual SinglePointCrossover(Individual p1, Individual p2, Random rnd)
        {
            int cut = rnd.Next(1, 3); // Точка розрізу 1 або 2
            var child = new Individual();
            for (int i = 0; i < 3; i++)
                child.Genes[i] = i < cut ? p1.Genes[i] : p2.Genes[i];
            return child;
        }

        // Мутація в допустимих межах
        private static void Mutate(Individual ind, double rate, Random rnd)
        {
            int[] maxBounds = { WindowOptions.Length, MinMatchOptions.Length, EntropyOptions.Length };
            for (int i = 0; i < 3; i++)
            {
                if (rnd.NextDouble() < rate)
                    ind.Genes[i] = rnd.Next(maxBounds[i]);
            }
        }
    }
}