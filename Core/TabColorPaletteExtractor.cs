using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Horizon.Stealth.Core;

public static class TabColorPaletteExtractor
{
    private static readonly HttpClient _http = new HttpClient();

    public static async Task<List<Color>> ExtractAsync(string url, string html)
    {
        var colors = new List<Color>();
        try
        {
            string? imgUrl = GetImageUrl(url, html);
            if (string.IsNullOrEmpty(imgUrl)) return colors;

            byte[] imageBytes = await _http.GetByteArrayAsync(imgUrl);
            colors = await Task.Run(() => AnalyzeImage(imageBytes));
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
        return colors;
    }


	public static async Task<List<Color>> ExtractFromImageUrlAsync(string imageUrl)
    {
        var colors = new List<Color>();
        try
        {
            byte[] imageBytes = await _http.GetByteArrayAsync(imageUrl);
            colors = await Task.Run(() => AnalyzeImage(imageBytes));
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
        return colors;
    }

    public static async Task<List<Color>> ExtractFromBase64Async(string base64)
    {
        var colors = new List<Color>();
        try
        {
            string b64Data = base64;
            int commaIdx = base64.IndexOf(',');
            if (commaIdx >= 0) b64Data = base64.Substring(commaIdx + 1);
            byte[] imageBytes = Convert.FromBase64String(b64Data);
            colors = await Task.Run(() => AnalyzeImage(imageBytes));
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
        return colors;
    }

    private static string? GetImageUrl(string baseUrl, string html)
    {
        string? match = MatchRegex(html, @"<meta\s+property=""og:image""\s+content=""([^""]+)""");
        if (match == null) match = MatchRegex(html, @"<meta\s+name=""twitter:image""\s+content=""([^""]+)""");
        if (match == null) match = MatchRegex(html, @"<link\s+rel=""apple-touch-icon""[^>]*href=""([^""]+)""");
        if (match == null) match = MatchRegex(html, @"<link\s+rel=""(?:shortcut )?icon""[^>]*href=""([^""]+)""");

        if (match != null)
        {
            match = match.Replace("&amp;", "&");
            if (Uri.TryCreate(new Uri(baseUrl), match, out Uri? absoluteUri))
                return absoluteUri.ToString();
        }

        try 
        {
            return $"https://www.google.com/s2/favicons?domain={new Uri(baseUrl).Host}&sz=128";
        }
        catch { return null; }
    }

    private static string? MatchRegex(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static List<Color> AnalyzeImage(byte[] imageBytes)
    {
        var palette = new List<Color>();
        try
        {
            BitmapImage bmp = new BitmapImage();
            using (var ms = new MemoryStream(imageBytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
            }

            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            converted.Freeze();

            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            converted.CopyPixels(pixels, stride, 0);

            var colorCounts = new Dictionary<int, int>();
            int step = Math.Max(4, (pixels.Length / 4) / 2000) * 4;

            for (int i = 0; i < pixels.Length; i += step)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];

                if (a < 200) continue;

                if (!IsValidColor(r, g, b)) continue;

                int rB = (r / 16) * 16;
                int gB = (g / 16) * 16;
                int bB = (b / 16) * 16;
                int bucket = (rB << 16) | (gB << 8) | bB;

                if (colorCounts.ContainsKey(bucket)) colorCounts[bucket]++;
                else colorCounts[bucket] = 1;
            }

            var sorted = colorCounts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
            var distinct     = new List<Color>();
            var distinctHues = new List<float>();

            // First pass: accept if hue-diverse (>18°) OR RGB-diverse (>40)
            foreach (int c in sorted)
            {
                byte r = (byte)((c >> 16) & 0xFF);
                byte g = (byte)((c >> 8) & 0xFF);
                byte b = (byte)(c & 0xFF);
                var color = Color.FromRgb(r, g, b);
                float hue = GetHue(r, g, b);

                bool hueOk = distinctHues.All(h => HueDelta(h, hue) > 18f);
                bool rgbOk = distinct.All(d => ColorDistance(d, color) > 40);

                if (distinct.Count == 0 || hueOk || rgbOk)
                {
                    distinct.Add(color);
                    distinctHues.Add(hue);
                    if (distinct.Count >= 3) break;
                }
            }

            // Second pass: image may be genuinely monochromatic — fill remaining slots with RGB-only
            if (distinct.Count < 3)
            {
                foreach (int c in sorted)
                {
                    byte r = (byte)((c >> 16) & 0xFF);
                    byte g = (byte)((c >> 8) & 0xFF);
                    byte b = (byte)(c & 0xFF);
                    var color = Color.FromRgb(r, g, b);
                    if (distinct.All(d => ColorDistance(d, color) > 40))
                    {
                        distinct.Add(color);
                        if (distinct.Count >= 3) break;
                    }
                }
            }

            if (distinct.Count > 0) palette.AddRange(distinct);
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
        return palette;
    }

    private static bool IsValidColor(byte r, byte g, byte b)
    {
        if (r > 240 && g > 240 && b > 240) return false;
        if (r < 20 && g < 20 && b < 20) return false;
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        if (max - min < 15) return false;
        return true;
    }

    private static float GetHue(byte r, byte g, byte b)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = Math.Max(rf, Math.Max(gf, bf));
        float min = Math.Min(rf, Math.Min(gf, bf));
        float delta = max - min;
        if (delta < 0.001f) return 0f;
        float h;
        if      (max == rf) h = ((gf - bf) / delta) % 6f;
        else if (max == gf) h = (bf - rf) / delta + 2f;
        else                h = (rf - gf) / delta + 4f;
        h *= 60f;
        if (h < 0) h += 360f;
        return h;
    }

    private static float HueDelta(float h1, float h2)
    {
        float d = Math.Abs(h1 - h2);
        return d > 180f ? 360f - d : d;
    }

    private static double ColorDistance(Color c1, Color c2)
    {
        long r = c1.R - c2.R;
        long g = c1.G - c2.G;
        long b = c1.B - c2.B;
        return Math.Sqrt(r * r + g * g + b * b);
    }

    private static void LogError(Exception ex)
    {
        string tempLog = Path.Combine(Path.GetTempPath(), "extractor_error.log");
        File.AppendAllText(tempLog, $"{DateTime.Now}: {ex.Message}{Environment.NewLine}");
        try 
        {
            string targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(targetDir);
            File.Copy(tempLog, Path.Combine(targetDir, "extractor_error.log"), true);
        }
        catch { }
    }
}