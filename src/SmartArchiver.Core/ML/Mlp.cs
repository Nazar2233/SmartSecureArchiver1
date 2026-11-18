using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MlpLab
{
    public class Layer
    {
        public readonly int InSize;
        public readonly int OutSize;
        public readonly double[,] W;
        public readonly double[] B;
        public double[] Input = null!;
        public readonly double[] Output;
        public readonly double[] Delta;

        public Layer(int inSize, int outSize, Random rnd)
        {
            InSize = inSize;
            OutSize = outSize;
            W = new double[outSize, inSize];
            B = new double[outSize];
            Output = new double[outSize];
            Delta = new double[outSize];

            double limit = Math.Sqrt(6.0 / (inSize + outSize));
            for (int j = 0; j < outSize; j++)
                for (int i = 0; i < inSize; i++)
                    W[j, i] = (2 * rnd.NextDouble() - 1) * limit;
        }

        public static double Sigmoid(double z) => 1.0 / (1.0 + Math.Exp(-z));

        public double[] Forward(double[] input)
        {
            Input = input;
            for (int j = 0; j < OutSize; j++)
            {
                double z = B[j];
                for (int i = 0; i < InSize; i++)
                    z += W[j, i] * input[i];
                Output[j] = Sigmoid(z);
            }
            return Output;
        }
    }

    public class Mlp
    {
        public readonly Layer[] Layers;

        public Mlp(int[] sizes, int seed)
        {
            var rnd = new Random(seed);
            Layers = new Layer[sizes.Length - 1];
            for (int l = 0; l < Layers.Length; l++)
                Layers[l] = new Layer(sizes[l], sizes[l + 1], rnd);
        }

        public double[] Forward(double[] x)
        {
            double[] a = x;
            foreach (var layer in Layers)
                a = layer.Forward(a);
            return a;
        }

        public static double Loss(double[] output, double[] target)
        {
            double e = 0;
            for (int j = 0; j < output.Length; j++)
                e += 0.5 * (output[j] - target[j]) * (output[j] - target[j]);
            return e;
        }

        public void Backward(double[] target)
        {
            Layer last = Layers[^1];
            for (int j = 0; j < last.OutSize; j++)
            {
                double a = last.Output[j];
                last.Delta[j] = (a - target[j]) * a * (1 - a);
            }

            for (int l = Layers.Length - 2; l >= 0; l--)
            {
                Layer cur = Layers[l], next = Layers[l + 1];
                for (int i = 0; i < cur.OutSize; i++)
                {
                    double sum = 0;
                    for (int j = 0; j < next.OutSize; j++)
                        sum += next.W[j, i] * next.Delta[j];

                    double a = cur.Output[i];
                    cur.Delta[i] = sum * a * (1 - a);
                }
            }
        }

        public void Update(double eta)
        {
            foreach (var layer in Layers)
                for (int j = 0; j < layer.OutSize; j++)
                {
                    for (int i = 0; i < layer.InSize; i++)
                        layer.W[j, i] -= eta * layer.Delta[j] * layer.Input[i];
                    layer.B[j] -= eta * layer.Delta[j];
                }
        }
    }
}
