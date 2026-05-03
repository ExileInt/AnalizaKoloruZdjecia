using Accord.MachineLearning;
using AnalizaKoloruZdjęcia.Helpers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SemClassification
{
    public class ClassGmmModel
    {
        public string? ClassName { get; set; }

        public GaussianMixtureModel? Model { get; set; }
    }

    public class GmmClassifier
    {
        private readonly List<ClassGmmModel> _models = new();
        public bool IsReady => _models.Count > 0;

        public void TrainFromCsv(string datasetPath)
        {
            if (!File.Exists(datasetPath))
            {
                throw new FileNotFoundException(datasetPath);
            }

            string[] lines = File.ReadAllLines(datasetPath);

            Dictionary<string, List<double[]>> grouped = new Dictionary<string, List<double[]>>();

            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(',');

                if (parts.Length < 7) continue;

                string className = parts[1];

                if (!double.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double l)) continue;

                if (!double.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out double a)) continue;

                if (!double.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out double b)) continue;

                double[] feature =
                {
                    NormalizeL(l),
                    NormalizeAB(a),
                    NormalizeAB(b)
                };

                if (!grouped.ContainsKey(className))
                {
                    grouped[className] = new List<double[]>();
                }

                grouped[className].Add(feature);
            }

            _models.Clear();

            foreach (KeyValuePair<string, List<double[]>> kv in grouped)
            {
                double[][] samples = kv.Value.ToArray();

                if (samples.Length == 0)
                    continue;

                // stabilizacja numeryczna (ważne dla GMM)
                for (int i = 0; i < samples.Length; i++)
                {
                    for (int j = 0; j < samples[i].Length; j++)
                    {
                        samples[i][j] += 1e-6;
                    }
                }

                // 3. dobór liczby komponentów
                int components = samples.Length < 30 ? 1 : samples.Length < 100 ? 2 : 3;

                GaussianMixtureModel gmm = new GaussianMixtureModel(components);

                // 4. TRENING
                gmm.Learn(samples);

                _models.Add(new ClassGmmModel
                {
                    ClassName = kv.Key,
                    Model = gmm
                });

            }
        }

        public (string className, double[] probabilities) Predict(double[] sample)
        {
            if (_models.Count == 0)
            {
                throw new InvalidOperationException("Models not trained.");
            }

            double[] scores = new double[_models.Count];

            double maxScore = double.NegativeInfinity;

            int bestIndex = -1;

            for (int i = 0; i < _models.Count; i++)
            {
                scores[i] = _models[i].Model?.ToMixtureDistribution().LogLikelihood(new[] { sample }) ?? double.NegativeInfinity;

                if (scores[i] > maxScore)
                {
                    maxScore = scores[i];
                    bestIndex = i;
                }
            }

            double max = scores.Max();

            double sum = 0;

            double[] probs = new double[scores.Length];

            for (int i = 0; i < scores.Length; i++)
            {
                probs[i] = Math.Exp(scores[i] - max);
                sum += probs[i];
            }

            for (int i = 0; i < probs.Length; i++)
            {
                probs[i] /= sum;
            }

            return (_models[bestIndex].ClassName ?? string.Empty, probs);
        }

        public string BuildProbabilityReport(double[] probabilities)
        {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < _models.Count; i++)
            {
                sb.AppendLine($"{_models[i].ClassName}: {probabilities[i] * 100:F2}%");
            }

            return sb.ToString();
        }

        public (string matchedClass, string classProbabilities) AnalyzeImage(byte[] fileBytes)
        {
            const int gridCols = 10;
            const int gridRows = 10;
            const int numCells = gridCols * gridRows;

            Dictionary<string, int> patchVotes = new Dictionary<string, int>();

            Dictionary<string, double> patchProbabilities = new Dictionary<string, double>();

            using MemoryStream ms = new MemoryStream(fileBytes);
            using Bitmap tempBitmap = new Bitmap(ms);

            using Bitmap bitmap = new Bitmap(tempBitmap);

            BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int bytes = Math.Abs(bmpData.Stride) * bitmap.Height;

                byte[] rgbValues = new byte[bytes];

                Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);

                int width = bitmap.Width;

                int height = bitmap.Height;

                object sync = new object();

                Parallel.For(0, numCells, cellIndex =>
                {
                    int cellX = cellIndex % gridCols;

                    int cellY = cellIndex / gridCols;

                    int startX = (int)(cellX * (width / (double)gridCols));

                    int endX = cellX == gridCols - 1 ? width : (int)((cellX + 1) * (width / (double)gridCols));

                    int startY = (int)(cellY * (height / (double)gridRows));

                    int endY = cellY == gridRows - 1 ? height : (int)((cellY + 1) * (height / (double)gridRows));

                    double sumL = 0;
                    double sumA = 0;
                    double sumB = 0;

                    int included = 0;

                    for (int y = startY; y < endY; y++)
                    {
                        for (int x = startX; x < endX; x++)
                        {
                            int pos = (y * bmpData.Stride) + (x * 4);

                            byte b = rgbValues[pos];

                            byte g = rgbValues[pos + 1];

                            byte r = rgbValues[pos + 2];

                            byte alpha = rgbValues[pos + 3];

                            if ((r == 0 && g == 0 && b == 0) || (r == 255 && g == 255 && b == 255) || alpha == 0)
                            {
                                continue;
                            }

                            (double l, double a, double b) lab = ColorHelper.RgbToLab(r, g, b);
                            sumL += lab.l;
                            sumA += lab.a;
                            sumB += lab.b;
                            included++;
                        }
                    }

                    if (included == 0)
                        return;

                    double[] sample =
                    {
                        NormalizeL(sumL / included),
                        NormalizeAB(sumA / included),
                        NormalizeAB(sumB / included)
                    };

                    (string className, double[] probabilities) result = Predict(sample);

                    double confidence = result.probabilities.Max();

                    lock (sync)
                    {
                        if (!patchVotes.ContainsKey(result.className))
                        {
                            patchVotes[result.className] = 0;

                            patchProbabilities[result.className] = 0;
                        }

                        patchVotes[result.className]++;

                        patchProbabilities[result.className] += confidence;
                    }
                });
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }

            if (patchVotes.Count == 0)
            {
                return ("Nieznana", "Brak danych");
            }

            string bestClass = patchVotes.OrderByDescending(x => x.Value).ThenByDescending(x => patchProbabilities[x.Key]).First().Key;

            StringBuilder sb = new StringBuilder();

            int totalVotes = patchVotes.Values.Sum();

            foreach (KeyValuePair<string, int> kv in patchVotes.OrderByDescending(x => x.Value))
            {
                double percent = kv.Value / (double)totalVotes * 100.0;
                sb.AppendLine($"{kv.Key}: {percent:F2}% ({kv.Value} patchy)");
            }

            return (bestClass, sb.ToString());
        }

        private static double NormalizeL(double l)
        {
            return l / 100.0;
        }

        private static double NormalizeAB(double value)
        {
            return (value + 128.0) / 255.0;
        }
    }
}