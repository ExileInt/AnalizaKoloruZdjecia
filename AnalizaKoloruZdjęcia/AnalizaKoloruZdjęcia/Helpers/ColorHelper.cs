using System;
using System.Drawing;

namespace AnalizaKoloruZdjęcia.Helpers
{
    public static class ColorHelper
    {
        public static ((double h, double s, double l) hsl, (double h, double s, double v) hsv, (double h, double s, double i) hsi, (double l, double a, double b) lab) RgbToAll(byte r, byte g, byte b)
        {
            float fr = r / 255f;
            float fg = g / 255f;
            float fb = b / 255f;

            float max = Math.Max(fr, Math.Max(fg, fb));
            float min = Math.Min(fr, Math.Min(fg, fb));
            float d = max - min;

            float h = 0f;
            if (max != min)
            {
                if (max == fr)
                    h = (fg - fb) / d + (fg < fb ? 6f : 0f);
                else if (max == fg)
                    h = (fb - fr) / d + 2f;
                else if (max == fb)
                    h = (fr - fg) / d + 4f;

                h /= 6f;
            }
            double hDegrees = h * 360;

            // HSL
            float l = (max + min) / 2f;
            float s_hsl = max == min ? 0f : (l > 0.5f ? d / (2f - max - min) : d / (max + min));

            // HSV
            float v = max;
            float s_hsv = max == 0 ? 0 : d / max;

            // HSI
            float i = (fr + fg + fb) / 3f;
            float s_hsi = i == 0 ? 0 : 1 - (min / i);

            // CIELAB (simplified D65 illuminant, 2° observer)
            double rLinear = PivotRgb(r / 255.0);
            double gLinear = PivotRgb(g / 255.0);
            double bLinear = PivotRgb(b / 255.0);

            // Observer. = 2°, Illuminant = D65
            double x = rLinear * 0.4124 + gLinear * 0.3576 + bLinear * 0.1805;
            double y = rLinear * 0.2126 + gLinear * 0.7152 + bLinear * 0.0722;
            double z = rLinear * 0.0193 + gLinear * 0.1192 + bLinear * 0.9505;

            double xn = 0.95047;
            double yn = 1.00000;
            double zn = 1.08883;

            double l_lab = 116.0 * PivotXyz(y / yn) - 16.0;
            double a_lab = 500.0 * (PivotXyz(x / xn) - PivotXyz(y / yn));
            double b_lab = 200.0 * (PivotXyz(y / yn) - PivotXyz(z / zn));

            return ((hDegrees, s_hsl * 100, l * 100), (hDegrees, s_hsv * 100, v * 100), (hDegrees, s_hsi * 100, i * 100), (l_lab, a_lab, b_lab));
        }

        public static double PivotRgb(double n)
        {
            return (n > 0.04045 ? Math.Pow((n + 0.055) / 1.055, 2.4) : n / 12.92) * 100.0;
        }

        public static double PivotXyz(double n)
        {
            return n > 0.008856 ? Math.Pow(n, 1.0 / 3.0) : (7.787 * n + 16.0 / 116.0);
        }

        public static (double h, double s, double l) RgbToHsl(byte r, byte g, byte b)
        {
            float fr = r / 255f;
            float fg = g / 255f;
            float fb = b / 255f;

            float max = Math.Max(fr, Math.Max(fg, fb));
            float min = Math.Min(fr, Math.Min(fg, fb));

            float h = 0f;
            float s = 0f;
            float l = (max + min) / 2f;

            if (max == min)
            {
                h = 0f;
                s = 0f; 
            }
            else
            {
                float d = max - min;
                s = l > 0.5f ? d / (2f - max - min) : d / (max + min);

                if (max == fr)
                    h = (fg - fb) / d + (fg < fb ? 6f : 0f);
                else if (max == fg)
                    h = (fb - fr) / d + 2f;
                else if (max == fb)
                    h = (fr - fg) / d + 4f;

                h /= 6f;
            }

            return (h * 360, s * 100, l * 100);
        }

        public static (double h, double s, double v) RgbToHsv(byte r, byte g, byte b)
        {
            float fr = r / 255f;
            float fg = g / 255f;
            float fb = b / 255f;

            float max = Math.Max(fr, Math.Max(fg, fb));
            float min = Math.Min(fr, Math.Min(fg, fb));
            float d = max - min;

            float h = 0f;
            float s = max == 0 ? 0 : d / max;
            float v = max;

            if (max == min)
            {
                h = 0f;
            }
            else
            {
                if (max == fr)
                    h = (fg - fb) / d + (fg < fb ? 6f : 0f);
                else if (max == fg)
                    h = (fb - fr) / d + 2f;
                else if (max == fb)
                    h = (fr - fg) / d + 4f;

                h /= 6f;
            }

            return (h * 360, s * 100, v * 100);
        }

        public static (double h, double s, double i) RgbToHsi(byte r, byte g, byte b)
        {
            float fr = r / 255f;
            float fg = g / 255f;
            float fb = b / 255f;

            float i = (fr + fg + fb) / 3f;
            float s = i == 0 ? 0 : 1 - (Math.Min(fr, Math.Min(fg, fb)) / i);

            float h = 0f;
            if (s != 0)
            {
                float num = 0.5f * ((fr - fg) + (fr - fb));
                float den = (float)Math.Sqrt((fr - fg) * (fr - fg) + (fr - fb) * (fg - fb));

                float theta = (float)Math.Acos(den == 0 ? 0 : num / den);
                h = fb > fg ? 2 * (float)Math.PI - theta : theta;
            }

            return (h * 180 / Math.PI, s * 100, i * 100);
        }

        public static System.Drawing.Color HslToRgb(double h, double s, double l)
        {
            h /= 360f;
            s /= 100f;
            l /= 100f;

            double r, g, b;

            if (s == 0)
            {
                r = g = b = l; // achromatic
            }
            else
            {
                var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                var p = 2 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3.0);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3.0);
            }

            return System.Drawing.Color.FromArgb((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        public static System.Drawing.Color HsvToRgb(double h, double s, double v)
        {
            s /= 100f;
            v /= 100f;

            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;

            double r = 0, g = 0, b = 0;

            if (h >= 0 && h < 60) { r = c; g = x; b = 0; }
            else if (h >= 60 && h < 120) { r = x; g = c; b = 0; }
            else if (h >= 120 && h < 180) { r = 0; g = c; b = x; }
            else if (h >= 180 && h < 240) { r = 0; g = x; b = c; }
            else if (h >= 240 && h < 300) { r = x; g = 0; b = c; }
            else if (h >= 300 && h < 360) { r = c; g = 0; b = x; }

            return System.Drawing.Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        public static System.Drawing.Color HsiToRgb(double h, double s, double i)
        {
            h = h * Math.PI / 180.0;
            s /= 100f;
            i /= 100f;

            double r = 0, g = 0, b = 0;

            if (h >= 0 && h < 2 * Math.PI / 3)
            {
                b = i * (1 - s);
                r = i * (1 + (s * Math.Cos(h)) / Math.Cos(Math.PI / 3 - h));
                g = 3 * i - (r + b);
            }
            else if (h >= 2 * Math.PI / 3 && h < 4 * Math.PI / 3)
            {
                h -= 2 * Math.PI / 3;
                r = i * (1 - s);
                g = i * (1 + (s * Math.Cos(h)) / Math.Cos(Math.PI / 3 - h));
                b = 3 * i - (r + g);
            }
            else if (h >= 4 * Math.PI / 3 && h <= 2 * Math.PI)
            {
                h -= 4 * Math.PI / 3;
                g = i * (1 - s);
                b = i * (1 + (s * Math.Cos(h)) / Math.Cos(Math.PI / 3 - h));
                r = 3 * i - (g + b);
            }

            r = Math.Clamp(r, 0, 1);
            g = Math.Clamp(g, 0, 1);
            b = Math.Clamp(b, 0, 1);

            return System.Drawing.Color.FromArgb((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
        }

        public static System.Drawing.Color LabToRgb(double l_lab, double a_lab, double b_lab)
        {
            double y = (l_lab + 16.0) / 116.0;
            double x = a_lab / 500.0 + y;
            double z = y - b_lab / 200.0;

            double xn = 0.95047;
            double yn = 1.00000;
            double zn = 1.08883;

            x = xn * PivotXyzReverse(x);
            y = yn * PivotXyzReverse(y);
            z = zn * PivotXyzReverse(z);

            x /= 100.0;
            y /= 100.0;
            z /= 100.0;

            double rLinear = x *  3.2406 + y * -1.5372 + z * -0.4986;
            double gLinear = x * -0.9689 + y *  1.8758 + z *  0.0415;
            double bLinear = x *  0.0557 + y * -0.2040 + z *  1.0570;

            int r = (int)Math.Round(PivotRgbReverse(rLinear) * 255.0);
            int g = (int)Math.Round(PivotRgbReverse(gLinear) * 255.0);
            int b = (int)Math.Round(PivotRgbReverse(bLinear) * 255.0);

            r = Math.Clamp(r, 0, 255);
            g = Math.Clamp(g, 0, 255);
            b = Math.Clamp(b, 0, 255);

            return System.Drawing.Color.FromArgb(r, g, b);
        }

        private static double PivotXyzReverse(double n)
        {
            double n3 = Math.Pow(n, 3.0);
            return n3 > 0.008856 ? n3 : (n - 16.0 / 116.0) / 7.787;
        }

        private static double PivotRgbReverse(double n)
        {
            return n > 0.0031308 ? 1.055 * Math.Pow(n, 1.0 / 2.4) - 0.055 : 12.92 * n;
        }
    }
}