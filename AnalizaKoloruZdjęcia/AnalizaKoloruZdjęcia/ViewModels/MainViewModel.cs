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

using System.Drawing.Imaging;

namespace AnalizaKoloruZdjęcia.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private BitmapImage? _selectedImageSource;
        private BitmapImage? _generatedImageSource;
        private string _colorAnalysisResult = string.Empty;
        private double _hueThreshold = 30.0;
        private string _currentFilePath = string.Empty;

        public double HueThreshold
        {
            get { return _hueThreshold; }
            set
            {
                if (_hueThreshold != value)
                {
                    _hueThreshold = value;
                    OnPropertyChanged(nameof(HueThreshold));
                    
                    // Reload image with new threshold if one is selected
                    if (!string.IsNullOrEmpty(_currentFilePath))
                    {
                        LoadImage(_currentFilePath);
                    }
                }
            }
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

        public MainViewModel()
        {
            SelectImageCommand = new CommandHandler(() =>
            {
                SelectImage();
            });

            DropImageCommand = new CommandHandler<string[]>(OnFilesDropped);
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
            try
            {
                _currentFilePath = filePath;
                byte[] fileBytes = File.ReadAllBytes(filePath);
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

            await Task.Run(() =>
            {
                try
                {
                    double avgHslH = 0, avgHslS = 0, avgHslL = 0;
                    double avgHsvH = 0, avgHsvS = 0, avgHsvV = 0;
                    double avgHsiH = 0, avgHsiS = 0, avgHsiI = 0;

                    int pixelCount = 0;

                    byte[] imageBytes = File.ReadAllBytes(filePath);
                    using (MemoryStream ms = new MemoryStream(imageBytes))
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
                        
                        double sumHslH = 0, sumHslS = 0, sumHslL = 0;
                        double sumHsvH = 0, sumHsvS = 0, sumHsvV = 0;
                        double sumHsiH = 0, sumHsiS = 0, sumHsiI = 0;

                        object lockObj = new object();

                        int width = bitmap.Width;
                        int height = bitmap.Height;

                        int centerPosition = ((height / 2) * bmpData.Stride) + ((width / 2) * 4);
                        byte centerB = rgbValues[centerPosition];
                        byte centerG = rgbValues[centerPosition + 1];
                        byte centerR = rgbValues[centerPosition + 2];

                        var (centerHsl, _, _) = ColorHelper.RgbToAll(centerR, centerG, centerB);
                        double centerH = centerHsl.h;

                        // Threshold for how far a hue can deviate from the center point's hue to be included
                        double hueThreshold = _hueThreshold; 

                        int includedPixels = 0;

                        Parallel.For(0, height, y =>
                        {
                            double localHslH = 0, localHslS = 0, localHslL = 0;
                            double localHsvH = 0, localHsvS = 0, localHsvV = 0;
                            double localHsiH = 0, localHsiS = 0, localHsiI = 0;
                            int localIncluded = 0;

                            for (int x = 0; x < width; x++)
                            {
                                int position = (y * bmpData.Stride) + (x * 4);
                                byte b = rgbValues[position];
                                byte g = rgbValues[position + 1];
                                byte r = rgbValues[position + 2];

                                var (hsl, hsv, hsi) = ColorHelper.RgbToAll(r, g, b);

                                // Check if the hue is close enough to the center hue (accounting for wrap-around)
                                double hueDiff = Math.Abs(hsl.h - centerH);
                                if (hueDiff > 180) hueDiff = 360 - hueDiff;
                                
                                if (hueDiff <= hueThreshold)
                                {
                                    localHslH += hsl.h; localHslS += hsl.s; localHslL += hsl.l;
                                    localHsvH += hsv.h; localHsvS += hsv.s; localHsvV += hsv.v;
                                    localHsiH += hsi.h; localHsiS += hsi.s; localHsiI += hsi.i;
                                    localIncluded++;
                                }
                                else
                                {
                                    // Set non-matching pixels to white
                                    rgbValues[position] = 255;     // B
                                    rgbValues[position + 1] = 255; // G
                                    rgbValues[position + 2] = 255; // R
                                    rgbValues[position + 3] = 255; // A (if needed)
                                }
                            }

                            lock (lockObj)
                            {
                                sumHslH += localHslH; sumHslS += localHslS; sumHslL += localHslL;
                                sumHsvH += localHsvH; sumHsvS += localHsvS; sumHsvV += localHsvV;
                                sumHsiH += localHsiH; sumHsiS += localHsiS; sumHsiI += localHsiI;
                                includedPixels += localIncluded;
                            }
                        });

                        // Copy modified bytes back to the bitmap and unlock
                        System.Runtime.InteropServices.Marshal.Copy(rgbValues, 0, bmpData.Scan0, bytes);
                        bitmap.UnlockBits(bmpData);
                        
                        // Save the filtered image to a file
                        string outputDir = Path.GetDirectoryName(filePath);
                        string originalFileName = Path.GetFileNameWithoutExtension(filePath);
                        string outputFileName = $"{originalFileName}_filtered.png";
                        string outputPath = Path.Combine(outputDir, outputFileName);
                        bitmap.Save(outputPath, ImageFormat.Png);

                        int validPixelCount = includedPixels > 0 ? includedPixels : 1; // Prevent division by zero

                        avgHslH = sumHslH / validPixelCount; avgHslS = sumHslS / validPixelCount; avgHslL = sumHslL / validPixelCount;
                        avgHsvH = sumHsvH / validPixelCount; avgHsvS = sumHsvS / validPixelCount; avgHsvV = sumHsvV / validPixelCount;
                        avgHsiH = sumHsiH / validPixelCount; avgHsiS = sumHsiS / validPixelCount; avgHsiI = sumHsiI / validPixelCount;

                        // Create the generated image
                        int imgWidth = 300;
                        int imgHeight = 200;
                        using (Bitmap resultBmp = new Bitmap(imgWidth, imgHeight))
                        {
                            System.Drawing.Color colorHsl = ColorHelper.HslToRgb(avgHslH, avgHslS, avgHslL);
                            System.Drawing.Color colorHsv = ColorHelper.HsvToRgb(avgHsvH, avgHsvS, avgHsvV);
                            System.Drawing.Color colorHsi = ColorHelper.HsiToRgb(avgHsiH, avgHsiS, avgHsiI);

                            using (Graphics graphics = Graphics.FromImage(resultBmp))
                            {
                                int colWidth = imgWidth / 3;
                                graphics.FillRectangle(new SolidBrush(colorHsl), 0, 0, colWidth, imgHeight);
                                graphics.FillRectangle(new SolidBrush(colorHsv), colWidth, 0, colWidth, imgHeight);
                                graphics.FillRectangle(new SolidBrush(colorHsi), colWidth * 2, 0, imgWidth - (colWidth * 2), imgHeight);
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
                    }

                    }

                    if (pixelCount > 0)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ColorAnalysisResult = $"Średnie wartości HSL (kolumna lewa):\n" +
                                                  $"H: {avgHslH:F2}°, S: {avgHslS:F2}%, L: {avgHslL:F2}%\n\n" +
                                                  $"Średnie wartości HSV (kolumna środkowa):\n" +
                                                  $"H: {avgHsvH:F2}°, S: {avgHsvS:F2}%, V: {avgHsvV:F2}%\n\n" +
                                                  $"Średnie wartości HSI (kolumna prawa):\n" +
                                                  $"H: {avgHsiH:F2}°, S: {avgHsiS:F2}%, I: {avgHsiI:F2}%";
                        });
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
