using Microsoft.Win32;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Drawing;
using AnalizaKoloruZdjęcia.Helpers;
using Accord.MachineLearning;
using Accord.Statistics;

using System.Drawing.Imaging;

namespace AnalizaKoloruZdjęcia.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        #region Zmienne i Commandy
        private BitmapImage? _selectedImageSource;
        private BitmapImage? _generatedImageSource;
        private string _colorAnalysisResult = string.Empty;
        private string _currentFilePath = string.Empty;
        private int _analyzedPixelCount = 0;
        private double _analysisProgress = 0;
        private string _progressText = string.Empty;

        public double AnalysisProgress
        {
            get { return _analysisProgress; }
            set { _analysisProgress = value; OnPropertyChanged(nameof(AnalysisProgress)); }
        }

        public string ProgressText
        {
            get { return _progressText; }
            set { _progressText = value; OnPropertyChanged(nameof(ProgressText)); }
        }

        public string ColorAnalysisResult
        {
            get { return _colorAnalysisResult; }
            set
            {
                _colorAnalysisResult = value;
                OnPropertyChanged(nameof(ColorAnalysisResult));
            }
        }

        public BitmapImage? SelectedImageSource
        {
            get { return _selectedImageSource; }
            set
            {
                _selectedImageSource = value;
                OnPropertyChanged(nameof(SelectedImageSource));
            }
        }

        public BitmapImage? GeneratedImageSource
        {
            get { return _generatedImageSource; }
            set
            {
                _generatedImageSource = value;
                OnPropertyChanged(nameof(GeneratedImageSource));
            }
        }
        public ICommand SelectImageCommand { get; }
        public ICommand DropImageCommand { get; }
        public ICommand GenerateDatasetCommand { get; }
        public ICommand KFoldDiagnosticsCommand { get; }
        #endregion
        public MainViewModel()
        {
            SelectImageCommand = new CommandHandler(() =>
            {
                SelectImage();
            });

            DropImageCommand = new CommandHandler<string[]>(OnFilesDropped);

            GenerateDatasetCommand = new CommandHandler(GenerateDataset);

            KFoldDiagnosticsCommand = new CommandHandler(RunKFoldDiagnostics);
        }

        private async void RunKFoldDiagnostics()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Obrazy (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
                Title = "Wybierz obrazy do diagnostyki Leave-One-Quadrant-Out",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string[] files = openFileDialog.FileNames;
                if (files.Length == 0) return;

                ColorAnalysisResult = $"Trwa diagnostyka (leave-one-quadrant-out) dla {files.Length} plików...";

                string txtOutputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diagnostics_results.txt");

                await Task.Run(() =>
                {
                    try
                    {
                        AnalysisProgress = 0;
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("=== Wyniki Leave-One-Quadrant-Out (4 foldy) ===");

                        int processedFiles = 0;
                        int totalCorrect = 0;
                        int totalFolds = 0;

                        double sumRatio = 0.0;
                        int ratioCount = 0;

                        foreach (string filePath in files)
                        {
                            string fileName = Path.GetFileName(filePath);
                            string fileClass = Path.GetFileNameWithoutExtension(filePath); // zakłada się, że klasa jest w nawiasach lub nazwie

                            Application.Current.Dispatcher.Invoke(() => { ProgressText = $"Analiza {filePath}..."; });

                            byte[] imageBytes = File.ReadAllBytes(filePath);
                            using (MemoryStream ms = new MemoryStream(imageBytes))
                            using (Bitmap tempBitmap = new Bitmap(ms))
                            using (Bitmap bitmap = new Bitmap(tempBitmap))
                            {
                                BitmapData bmpData = bitmap.LockBits(
                                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                                    ImageLockMode.ReadOnly,
                                    PixelFormat.Format32bppArgb);

                                int bytes = Math.Abs(bmpData.Stride) * bitmap.Height;
                                byte[] rgbValues = new byte[bytes];
                                System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);
                                bitmap.UnlockBits(bmpData);

                                int height = bitmap.Height;
                                int width = bitmap.Width;

                                // Zdefiniuj 4 kwadranty (0: TL, 1: TR, 2: BL, 3: BR)
                                var quadrants = new (double L, double A, double B)[4];
                                var quadCounts = new int[4];

                                int gridCols = 2;
                                int gridRows = 2;

                                Parallel.For(0, 4, qIndex =>
                                {
                                    int qX = qIndex % 2;
                                    int qY = qIndex / 2;

                                    int startY = qY * (height / 2);
                                    int endY = (qY + 1) * (height / 2);
                                    int startX = qX * (width / 2);
                                    int endX = (qX + 1) * (width / 2);

                                    double sumL = 0, sumA = 0, sumB = 0;
                                    int localCount = 0;

                                    for (int y = startY; y < endY; y++)
                                    {
                                        for (int x = startX; x < endX; x++)
                                        {
                                            int pos = (y * bmpData.Stride) + (x * 4);
                                            byte b = rgbValues[pos];
                                            byte g = rgbValues[pos + 1];
                                            byte r = rgbValues[pos + 2];
                                            byte a = rgbValues[pos + 3];

                                            if (a == 0 || (r == 0 && g == 0 && b == 0) || (r == 255 && g == 255 && b == 255)) continue;

                                            var (_, _, _, lab) = ColorHelper.RgbToAll(r, g, b);
                                            sumL += lab.l; sumA += lab.a; sumB += lab.b;
                                            localCount++;
                                        }
                                    }
                                    if(localCount > 0)
                                    {
                                        quadrants[qIndex] = (sumL / localCount, sumA / localCount, sumB / localCount);
                                        quadCounts[qIndex] = localCount;
                                    }
                                });

                                // Generuj centroidy klasowe jeśli by istniały inne - w ujęciu symulowanym po prostu oblicz centroid z 3 kwadrantów reszty
                                sb.AppendLine($"\nObraz: {fileName} (Klasa: {fileClass})");

                                for (int testQuad = 0; testQuad < 4; testQuad++)
                                {
                                    if (quadCounts[testQuad] == 0) continue;
                                    totalFolds++;

                                    double trainL = 0, trainA = 0, trainB = 0;
                                    for(int trainQuad = 0; trainQuad < 4; trainQuad++)
                                    {
                                        if (trainQuad != testQuad)
                                        {
                                            trainL += quadrants[trainQuad].L;
                                            trainA += quadrants[trainQuad].A;
                                            trainB += quadrants[trainQuad].B;
                                        }
                                    }
                                    trainL /= 3.0; trainA /= 3.0; trainB /= 3.0;

                                    // Distance to own centroid (from 3 other quads)
                                    double testL = quadrants[testQuad].L;
                                    double testA = quadrants[testQuad].A;
                                    double testB = quadrants[testQuad].B;

                                    double distToOwn = Math.Sqrt( Math.Pow((testL - trainL)/100.0, 2) + Math.Pow((testA - trainA)/255.0, 2) + Math.Pow((testB - trainB)/255.0, 2) );

                                    // Symulacja nearest foreign centroid: Ponieważ ładujemy osobne pliki z dysku dla eksperymentu nie mamy bazy globalnej centroidów klas, dla celu diagnostyki własnej mierzymy więc tylko intra-class odchyły
                                    // lub ładujemy dataset_analysis zeby znalezc obce. Poniżej tylko logujemy dystans
                                    sb.AppendLine($"  Fold {testQuad+1}: Dystans próbki testowej do trenowanego centroida własnej klasy = {distToOwn:F4}");
                                    sumRatio += distToOwn;
                                    ratioCount++;
                                }
                            }
                            processedFiles++;
                            Application.Current.Dispatcher.Invoke(() => { AnalysisProgress = (processedFiles / (double)files.Length) * 100; });
                        }

                        if (ratioCount > 0)
                        {
                            sb.AppendLine($"\nŚredni dystans odchyłu (intra-class test quad distance): {(sumRatio/ratioCount):F4}");
                        }

                        File.WriteAllText(txtOutputPath, sb.ToString(), Encoding.UTF8);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            AnalysisProgress = 100;
                            ProgressText = "Diagnostyka zakończona.";
                            ColorAnalysisResult = $"Diagnostyka Leave-One-Quadrant-Out (4 foldy) zakończona.\nZapisano wyniki do: {txtOutputPath}";
                        });
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ColorAnalysisResult = $"Błąd podczas diagnostyki:\n{ex.Message}";
                        });
                    }
                });
            }
        }

        private async void GenerateDataset()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Obrazy (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
                Title = "Wybierz obrazy do zbioru testowego",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string[] files = openFileDialog.FileNames;
                if (files.Length == 0) return;

                ColorAnalysisResult = $"Trwa generowanie zbioru testowego dla {files.Length} plików...";

                string txtOutputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dataset_analysis.csv");

                await Task.Run(() =>
                {
                    try
                    {
                        AnalysisProgress = 0;

                        StringBuilder sb = new StringBuilder();
                        // CSV Header
                        sb.AppendLine("FileName,Class,CellIndex,Count,LabL,LabA,LabB");

                        int processedFiles = 0;
                        foreach (string filePath in files)
                        {
                            string fileName = Path.GetFileName(filePath);
                            string fileClass = Path.GetFileNameWithoutExtension(filePath); // Using file name as class
                            string baseName = Path.GetFileNameWithoutExtension(filePath);

                            // klasa = część przed "_"
                            string newClassName;

                            int underscoreIndex = baseName.IndexOf('_');
                            if (underscoreIndex > 0)
                            {
                                newClassName = baseName.Substring(0, underscoreIndex);
                            }
                            else
                            {
                                newClassName = baseName;
                            }

                            Application.Current.Dispatcher.Invoke(() => { ProgressText = $"Plik {filePath}..."; });

                            byte[] imageBytes = File.ReadAllBytes(filePath);
                            using (MemoryStream ms = new MemoryStream(imageBytes))
                            using (Bitmap tempBitmap = new Bitmap(ms))
                            using (Bitmap bitmap = new Bitmap(tempBitmap))
                            {
                                BitmapData bmpData = bitmap.LockBits(
                                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                                    ImageLockMode.ReadOnly,
                                    PixelFormat.Format32bppArgb);

                                int bytes = Math.Abs(bmpData.Stride) * bitmap.Height;
                                byte[] rgbValues = new byte[bytes];
                                System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);
                                bitmap.UnlockBits(bmpData);

                                int height = bitmap.Height;
                                int width = bitmap.Width;

                                int gridCols = 10;
                                int gridRows = 10;
                                int numCells = gridCols * gridRows;

                                //double[] cellSumHsvH = new double[numCells], cellSumHsvS = new double[numCells], cellSumHsvV = new double[numCells];
                                double[] cellSumLabL = new double[numCells], cellSumLabA = new double[numCells], cellSumLabB = new double[numCells];
                                int[] cellIncluded = new int[numCells];

                                Parallel.For(0, numCells, cellIndex =>
                                {
                                    int cellX = cellIndex % gridCols;
                                    int cellY = cellIndex / gridCols;

                                    int startY = (int)(cellY * (height / (double)gridRows));
                                    int endY = cellY == gridRows - 1 ? height : (int)((cellY + 1) * (height / (double)gridRows));

                                    int startX = (int)(cellX * (width / (double)gridCols));
                                    int endX = cellX == gridCols - 1 ? width : (int)((cellX + 1) * (width / (double)gridCols));

                                    //double localHsvH = 0, localHsvS = 0, localHsvV = 0;
                                    double localLabL = 0, localLabA = 0, localLabB = 0;
                                    int localIncluded = 0;

                                    for (int y = startY; y < endY; y++)
                                    {
                                        for (int x = startX; x < endX; x++)
                                        {
                                            int position = (y * bmpData.Stride) + (x * 4);
                                            byte b = rgbValues[position];
                                            byte g = rgbValues[position + 1];
                                            byte r = rgbValues[position + 2];
                                            byte a = rgbValues[position + 3];

                                            if ((r == 0 && g == 0 && b == 0) || a == 0 || (r == 255 && g == 255 && b == 255)) continue;

                                            var (_, _, _, lab) = ColorHelper.RgbToAll(r, g, b);

                                            //localHsvH += hsv.h; localHsvS += hsv.s; localHsvV += hsv.v;
                                            localLabL += lab.l; localLabA += lab.a; localLabB += lab.b;
                                            localIncluded++;
                                        }
                                    }

                                    //cellSumHsvH[cellIndex] = localHsvH; cellSumHsvS[cellIndex] = localHsvS; cellSumHsvV[cellIndex] = localHsvV;
                                    cellSumLabL[cellIndex] = localLabL; cellSumLabA[cellIndex] = localLabA; cellSumLabB[cellIndex] = localLabB;
                                    cellIncluded[cellIndex] = localIncluded;
                                });

                                for (int i = 0; i < numCells; i++)
                                {
                                    int cCount = cellIncluded[i];
                                    if (cCount > 0)
                                    {
                                        //string hsvH = (cellSumHsvH[i] / cCount).ToString("F2", CultureInfo.InvariantCulture);
                                        //string hsvS = (cellSumHsvS[i] / cCount).ToString("F2", CultureInfo.InvariantCulture);
                                       // string hsvV = (cellSumHsvV[i] / cCount).ToString("F2", CultureInfo.InvariantCulture);

                                        string labL = (cellSumLabL[i] / cCount).ToString("F2", CultureInfo.InvariantCulture);
                                        string labA = (cellSumLabA[i] / cCount).ToString("F2", CultureInfo.InvariantCulture);
                                        string labB = (cellSumLabB[i] / cCount).ToString("F2", CultureInfo.InvariantCulture);

                                        sb.AppendLine($"{fileName},{newClassName},{i},{cCount},{labL},{labA},{labB}");
                                    }
                                }
                            }
                            processedFiles++;
                            Application.Current.Dispatcher.Invoke(() => { AnalysisProgress = (processedFiles / (double)files.Length) * 100; });
                        }

                        File.WriteAllText(txtOutputPath, sb.ToString(), Encoding.UTF8);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            AnalysisProgress = 100;
                            ProgressText = "Zakończono.";
                            ColorAnalysisResult = $"Zbiór testowy wygenerowany pomyślnie:\nZapisano w: {txtOutputPath}";
                        });
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ColorAnalysisResult = $"Błąd podczas generowania zbioru testowego:\n{ex.Message}";
                        });
                    }
                });
            }
        }

        private void OnFilesDropped(string[]? files)
        {
            if (files != null && files.Length > 0)
            {
                string file = files[0];
                if (IsImageFile(file))
                {
                    LoadImage(file);
                }
            }
        }

        private bool IsImageFile(string filePath)
        {
            string extension = Path.GetExtension(filePath)?.ToLower() ?? string.Empty;
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".bmp" || extension == ".gif";
        }

        private void SelectImage()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Obrzay (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
                Title = "Wybierz obraz"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                LoadImage(openFileDialog.FileName);
            }
        }

        public async void LoadImage(string filePath)
        {
            // Load the image into memory stream first so WPF doesn't lock the file on disk
            byte[] fileBytes;
            try
            {
                _currentFilePath = filePath;
                fileBytes = await File.ReadAllBytesAsync(filePath);
                using (MemoryStream ms = new MemoryStream(fileBytes))
                {
                    BitmapImage bmpImage = new BitmapImage();
                    bmpImage.BeginInit();
                    bmpImage.StreamSource = ms;
                    bmpImage.CacheOption = BitmapCacheOption.OnLoad;
                    bmpImage.EndInit();
                    bmpImage.Freeze();
                    SelectedImageSource = bmpImage;
                }
            }
            catch (Exception ex)
            {
                ColorAnalysisResult = $"Nie można wczytać pliku do podglądu:\n{ex.Message}";
                return;
            }

            ColorAnalysisResult = $"Wybrano plik:\n{filePath}\n\nAnaliza w toku...";

            AnalysisProgress = 0;
            ProgressText = "Rozpoczynam podział powiązań...";

            await Task.Run(() =>
            {
                try
                {
                    double avgLabL = 0, avgLabA = 0, avgLabB = 0;

                    int pixelCount = 0;
                    string matchedClass = "Nieznana";
                    string classProbabilities = string.Empty;

                    using (MemoryStream ms = new MemoryStream(fileBytes))
                    {
                        using (Bitmap tempBitmap = new Bitmap(ms))
                        using (Bitmap bitmap = new Bitmap(tempBitmap))
                        {
                            pixelCount = bitmap.Width * bitmap.Height;

                            BitmapData bmpData = bitmap.LockBits(
                                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                                ImageLockMode.ReadWrite,
                                PixelFormat.Format32bppArgb);

                            int bytes = Math.Abs(bmpData.Stride) * bitmap.Height;
                            byte[] rgbValues = new byte[bytes];
                            System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);

                            //double sumHsvH = 0, sumHsvS = 0, sumHsvV = 0;
                            double sumLabL = 0, sumLabA = 0, sumLabB = 0;

                            int height = bitmap.Height;
                            int width = bitmap.Width;

                            int gridCols = 10;
                            int gridRows = 10;
                            int numCells = gridCols * gridRows;

                            //double[] cellSumHsvH = new double[numCells], cellSumHsvS = new double[numCells], cellSumHsvV = new double[numCells];
                            double[] cellSumLabL = new double[numCells], cellSumLabA = new double[numCells], cellSumLabB = new double[numCells];
                            int[] cellIncluded = new int[numCells];

                            Application.Current.Dispatcher.Invoke(() => { ProgressText = "Analiza partii siatki..."; });

                            Parallel.For(0, numCells, cellIndex =>
                            {
                                int cellX = cellIndex % gridCols;
                                int cellY = cellIndex / gridCols;

                                int startY = (int)(cellY * (height / (double)gridRows));
                                int endY = cellY == gridRows - 1 ? height : (int)((cellY + 1) * (height / (double)gridRows));

                                int startX = (int)(cellX * (width / (double)gridCols));
                                int endX = cellX == gridCols - 1 ? width : (int)((cellX + 1) * (width / (double)gridCols));

                                //double localHsvH = 0, localHsvS = 0, localHsvV = 0;
                                double localLabL = 0, localLabA = 0, localLabB = 0;
                                int localIncluded = 0;

                                for (int y = startY; y < endY; y++)
                                {
                                    for (int x = startX; x < endX; x++)
                                    {
                                        int position = (y * bmpData.Stride) + (x * 4);
                                        byte b = rgbValues[position];
                                        byte g = rgbValues[position + 1];
                                        byte r = rgbValues[position + 2];
                                        byte a = rgbValues[position + 3];

                                        // Skip completely black, white and transparent pixels
                                        if ((r == 0 && g == 0 && b == 0 ) || a == 0 || (r == 255 && g == 255 && b == 255)) continue;

                                        var (_, _, _, lab) = ColorHelper.RgbToAll(r, g, b);

                                        //localHsvH += hsv.h; localHsvS += hsv.s; localHsvV += hsv.v;
                                        localLabL += lab.l; localLabA += lab.a; localLabB += lab.b;
                                        localIncluded++;
                                    }
                                }

                                //cellSumHsvH[cellIndex] = localHsvH; cellSumHsvS[cellIndex] = localHsvS; cellSumHsvV[cellIndex] = localHsvV;
                                cellSumLabL[cellIndex] = localLabL; cellSumLabA[cellIndex] = localLabA; cellSumLabB[cellIndex] = localLabB;
                                cellIncluded[cellIndex] = localIncluded;

                                Application.Current.Dispatcher.Invoke(() => { AnalysisProgress += 100.0 / numCells; });
                            });

                            Application.Current.Dispatcher.Invoke(() => { ProgressText = "Sumuje i sprawdzam zestaw danych..."; });

                            int includedPixels = 0;
                            for (int i = 0; i < numCells; i++)
                            {
                                //sumHsvH += cellSumHsvH[i]; sumHsvS += cellSumHsvS[i]; sumHsvV += cellSumHsvV[i];
                                sumLabL += cellSumLabL[i]; sumLabA += cellSumLabA[i]; sumLabB += cellSumLabB[i];
                                includedPixels += cellIncluded[i];
                            }

                            _analyzedPixelCount = includedPixels;

                            // Copy modified bytes back to the bitmap and unlock
                            System.Runtime.InteropServices.Marshal.Copy(rgbValues, 0, bmpData.Scan0, bytes);
                            bitmap.UnlockBits(bmpData);
                            int validPixelCount = includedPixels > 0 ? includedPixels : 1;

                            avgLabL = sumLabL / validPixelCount; avgLabA = sumLabA / validPixelCount; avgLabB = sumLabB / validPixelCount;
                            

                            // Normalizacja przed zestawieniem (CIELAB odległości L:0..100, A:-128..127, B:-128..127) -> do [0, 1] i algorytmu w klasyfikatorze

                            string datasetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dataset_analysis.csv");
                            
                            // Przetwarzanie danych 
                            /*
                            if (File.Exists(datasetPath))
                            {
                                string[] lines = File.ReadAllLines(datasetPath);
                                var classDistances = new Dictionary<string, List<double>>();

                                // Skipped the first line (headers)
                                for (int d = 1; d < lines.Length; d++)
                                {
                                    string[] parts = lines[d].Split(',');
                                    if (parts.Length >= 4)
                                    {
                                        string currentClass = parts[1];

                                        if (parts.Length >= 7 &&
                                            double.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double dLabL) &&
                                            double.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out double dLabA) &&
                                            double.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out double dLabB))
                                        {
                                            // Normalized Euclidean distance in CIELAB space
                                            double nL = Math.Pow((dLabL - avgLabL) / 100.0, 2);
                                            double nA = Math.Pow((dLabA - avgLabA) / 255.0, 2);
                                            double nB = Math.Pow((dLabB - avgLabB) / 255.0, 2);

                                            double distance = Math.Sqrt(nL + nA + nB);

                                            if (!classDistances.ContainsKey(currentClass))
                                            {
                                                classDistances[currentClass] = new List<double>();
                                            }
                                            classDistances[currentClass].Add(distance);
                                        }
                                    }
                                }

                                if (classDistances.Count > 0)
                                {
                                    var classAverages = new Dictionary<string, double>();
                                    double totalInverseDistance = 0;

                                    foreach (var kvp in classDistances)
                                    {
                                        double sum = 0;
                                        foreach (var val in kvp.Value)
                                        {
                                            sum += val;
                                        }
                                        double avgDist = sum / kvp.Value.Count;
                                        classAverages[kvp.Key] = avgDist;
                                        // Avoiding division by zero
                                        totalInverseDistance += 1.0 / (avgDist + 0.0001);
                                    }

                                    double maxProb = -1;
                                    StringBuilder probSb = new StringBuilder();

                                    foreach (var kvp in classAverages)
                                    {
                                        double prob = (1.0 / (kvp.Value + 0.0001)) / totalInverseDistance * 100;
                                        probSb.AppendLine($"{kvp.Key}: {prob:F2}%");

                                        if (prob > maxProb)
                                        {
                                            maxProb = prob;
                                            matchedClass = kvp.Key;
                                        }
                                    }
                                    classProbabilities = probSb.ToString();
                                }
                            }
                            */

                            Application.Current.Dispatcher.Invoke(() => { ProgressText = "Tworzenie grafiki i zapis na dysk..."; });

                            // Create the generated image
                            int imgWidth = 400;
                            int imgHeight = 200;
                            using (Bitmap resultBmp = new Bitmap(imgWidth, imgHeight))
                            {
                                Color colorLab = ColorHelper.LabToRgb(avgLabL, avgLabA, avgLabB);

                                using (Graphics graphics = Graphics.FromImage(resultBmp))
                                {
                                    //int colWidth = imgWidth / 2;
                                    graphics.FillRectangle(new SolidBrush(colorLab), 0, 0, imgWidth, imgHeight);
                                }

                                using (MemoryStream memoryStream = new MemoryStream())
                                {
                                    resultBmp.Save(memoryStream, ImageFormat.Png);
                                    memoryStream.Position = 0;

                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        BitmapImage bitmapImage = new BitmapImage();
                                        bitmapImage.BeginInit();
                                        bitmapImage.StreamSource = memoryStream;
                                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                                        bitmapImage.EndInit();
                                        bitmapImage.Freeze();
                                        GeneratedImageSource = bitmapImage;
                                    });
                                }
                            }

                            Application.Current.Dispatcher.Invoke(() => { ProgressText = "Analiza zakończona!"; AnalysisProgress = 100; });
                        }

                        if (pixelCount > 0)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                ColorAnalysisResult = $"Rozpoznana nazwa (klasa): {matchedClass}\n\nPrawdopodobieństwa klas:\n{classProbabilities}\n" +
                                                      //$"Średnie wartości HSV (kolumna lewa):\n" +
                                                      //$"H: {avgHsvH:F2}°, S: {avgHsvS:F2}%, V: {avgHsvV:F2}%\n\n" +
                                                      //$"Średnie CIELAB (kolumna prawa):\n" +
                                                      $"L: {avgLabL:F2}, A: {avgLabA:F2}, B: {avgLabB:F2}\n\n" +
                                                      $"Na podstawie {_analyzedPixelCount} pikseli.\n" +
                                                      $"Co stanowi {(_analyzedPixelCount * 100.0 / pixelCount):F2}% wszystkich pikseli.";
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ColorAnalysisResult = $"Wystąpił błąd podczas analizy obrazu:\n{ex.Message}";
                    });
                }
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
