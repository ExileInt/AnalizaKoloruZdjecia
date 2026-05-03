using AnalizaKoloruZdjęcia.Helpers;
using Microsoft.Win32;
using SemClassification;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace AnalizaKoloruZdjęcia.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        #region Zmienne i Commandy
        private BitmapImage? _selectedImageSource;
        private BitmapImage? _generatedImageSource;
        private string _colorAnalysisResult = string.Empty;
        private string _currentFilePath = string.Empty;
        private double _analysisProgress = 0;
        private string _progressText = string.Empty;
        private readonly GmmClassifier _classifier;
        private readonly string _datasetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trainData.csv");
        private Visibility _selectImageVisibility = Visibility.Visible;
        private Visibility _trainVisibility = Visibility.Visible;
        private Visibility _diagnosticVisibility = Visibility.Collapsed;

        public Visibility SelectImageVisibility
        {
            get { return _selectImageVisibility; }
            set { _selectImageVisibility = value; OnPropertyChanged(nameof(SelectImageVisibility)); }
        }

        public Visibility TrainVisibility
        {
            get { return _trainVisibility; }
            set { _trainVisibility = value; OnPropertyChanged(nameof(TrainVisibility)); }
        }

        public Visibility DiagnosticVisibility
        {
            get { return _diagnosticVisibility; }
            set { _diagnosticVisibility = value; OnPropertyChanged(nameof(DiagnosticVisibility)); }
        }

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
            _classifier = new GmmClassifier();

            if (File.Exists(_datasetPath))
            {
                _classifier.TrainFromCsv(_datasetPath);
                SelectImageVisibility = Visibility.Visible;
                TrainVisibility = Visibility.Visible;
            }

            else
            {
                SelectImageVisibility = Visibility.Collapsed;
                TrainVisibility = Visibility.Visible;
            }

            //if (!_classifier.IsReady)
            //{
            //    throw new Exception("GMM training failed.");
            //}

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
                                    if (localCount > 0)
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
                                    for (int trainQuad = 0; trainQuad < 4; trainQuad++)
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

                                    double distToOwn = Math.Sqrt(Math.Pow((testL - trainL) / 100.0, 2) + Math.Pow((testA - trainA) / 255.0, 2) + Math.Pow((testB - trainB) / 255.0, 2));

                                    // Symulacja nearest foreign centroid: Ponieważ ładujemy osobne pliki z dysku dla eksperymentu nie mamy bazy globalnej centroidów klas, dla celu diagnostyki własnej mierzymy więc tylko intra-class odchyły
                                    // lub ładujemy dataset_analysis zeby znalezc obce. Poniżej tylko logujemy dystans
                                    sb.AppendLine($"  Fold {testQuad + 1}: Dystans próbki testowej do trenowanego centroida własnej klasy = {distToOwn:F4}");
                                    sumRatio += distToOwn;
                                    ratioCount++;
                                }
                            }
                            processedFiles++;
                            Application.Current.Dispatcher.Invoke(() => { AnalysisProgress = processedFiles / (double)files.Length * 100; });
                        }

                        if (ratioCount > 0)
                        {
                            sb.AppendLine($"\nŚredni dystans odchyłu (intra-class test quad distance): {sumRatio / ratioCount:F4}");
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

            OpenFileDialog openFileDialog = new OpenFileDialog
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

                string txtOutputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _datasetPath);

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

                                            (double labL, double labA, double labB) = ColorHelper.RgbToLab(r, g, b);

                                            localLabL += labL; localLabA += labA; localLabB += labB;
                                            localIncluded++;
                                        }
                                    }
                                    cellSumLabL[cellIndex] = localLabL; cellSumLabA[cellIndex] = localLabA; cellSumLabB[cellIndex] = localLabB;
                                    cellIncluded[cellIndex] = localIncluded;
                                });

                                for (int i = 0; i < numCells; i++)
                                {
                                    int cCount = cellIncluded[i];
                                    if (cCount > 0)
                                    {

                                        string labL = (cellSumLabL[i] / cCount).ToString("F2", CultureInfo.InvariantCulture);
                                        string labA = (cellSumLabA[i] / cCount).ToString("F2", CultureInfo.InvariantCulture);
                                        string labB = (cellSumLabB[i] / cCount).ToString("F2", CultureInfo.InvariantCulture);

                                        sb.AppendLine($"{fileName},{newClassName},{i},{cCount},{labL},{labA},{labB}");
                                    }
                                }
                            }
                            processedFiles++;
                            Application.Current.Dispatcher.Invoke(() => { AnalysisProgress = processedFiles / (double)files.Length * 100; });
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

                _classifier.TrainFromCsv(_datasetPath);

                if (!_classifier.IsReady)
                {
                    throw new Exception("GMM training failed.");
                }
                SelectImageVisibility = Visibility.Visible;
                TrainVisibility = Visibility.Visible;

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
                ColorAnalysisResult = $"Nie można wczytać pliku do podglądu: \n{ex.Message}";
                return;
            }

            ColorAnalysisResult = $"Wybrano plik:\n{filePath}\n\nAnaliza w toku...";

            AnalysisProgress = 0;
            ProgressText = "Rozpoczynam podział obrazu...";

            await Task.Run(() =>
            {
                try
                {
                    var patchFeatures = new List<double[]>();
                    object patchLock = new object();
                    int pixelCount = 0;
                    string matchedClass = "Nieznana";
                    string classProbabilities = string.Empty;

                    using (MemoryStream ms = new MemoryStream(fileBytes))
                    {
                        using (Bitmap tempBitmap = new Bitmap(ms))
                        using (Bitmap bitmap = new Bitmap(tempBitmap))
                        {
                            pixelCount = bitmap.Width * bitmap.Height;

                            BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

                            int bytes = Math.Abs(bmpData.Stride) * bitmap.Height;
                            byte[] rgbValues = new byte[bytes];
                            System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);

                            int height = bitmap.Height;
                            int width = bitmap.Width;

                            int gridCols = 10;
                            int gridRows = 10;
                            int numCells = gridCols * gridRows;

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
                                        if ((r == 0 && g == 0 && b == 0) || a == 0 || (r == 255 && g == 255 && b == 255)) continue;
                                        (double labL, double labA, double labB) = ColorHelper.RgbToLab(r, g, b);
                                        localLabL += labL; localLabA += labA; localLabB += labB;
                                        localIncluded++;
                                    }
                                }

                                if (localIncluded > 0)
                                {
                                    double avgL = localLabL / localIncluded;

                                    double avgA = localLabA / localIncluded;

                                    double avgB = localLabB / localIncluded;

                                    double[] feature = { avgL / 100.0, (avgA + 128.0) / 255.0, (avgB + 128.0) / 255.0 };

                                    lock (patchLock)
                                    {
                                        patchFeatures.Add(feature);
                                    }
                                }
                                cellIncluded[cellIndex] = localIncluded;

                                Application.Current.Dispatcher.Invoke(() => { AnalysisProgress += 100.0 / numCells; });
                            });

                            Application.Current.Dispatcher.Invoke(() => { ProgressText = "Sumuje i sprawdzam zestaw danych..."; });
                            Dictionary<string, int> votes = new Dictionary<string, int>();
                            Dictionary<string, double> classScores = new Dictionary<string, double>();
                            foreach (double[] patch in patchFeatures)
                            {
                                (string className, double[] probabilities) result = _classifier.Predict(patch);
                                double confidence = result.probabilities.Max();
                                if (!votes.ContainsKey(result.className))
                                {
                                    votes[result.className] = 0;
                                    classScores[result.className] = 0;
                                }
                                votes[result.className]++;
                                classScores[result.className] += confidence;
                            }
                            matchedClass = votes.OrderByDescending(x => x.Value).ThenByDescending(x => classScores[x.Key]).First().Key;
                            StringBuilder sb = new StringBuilder();
                            int totalVotes = votes.Values.Sum();
                            foreach (KeyValuePair<string, int> kv in votes.OrderByDescending(x => x.Value))
                            {
                                double p = kv.Value / (double)totalVotes * 100.0;
                                if (kv.Value == 1)
                                {
                                    sb.AppendLine($"{kv.Key}: {p:F2}% ({kv.Value} patch)");
                                }
                                else
                                {
                                    sb.AppendLine($"{kv.Key}: {p:F2}% ({kv.Value} patchy)");
                                }
                            }

                            classProbabilities = sb.ToString();
                            double avgLabL = patchFeatures.Average(x => x[0]) * 100.0;
                            double avgLabA = (patchFeatures.Average(x => x[1]) * 255.0) - 128.0;
                            double avgLabB = (patchFeatures.Average(x => x[2]) * 255.0) - 128.0;


                            // Przetwarzanie danych 

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
                                ColorAnalysisResult = $"Rozpoznana nazwa (klasa): {matchedClass}" +
                                $"\n\nPrawdopodobieństwa klas:\n{classProbabilities}\n";
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