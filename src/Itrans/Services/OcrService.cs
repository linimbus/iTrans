using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Itrans.Services;

public sealed class OcrService
{
    public async Task<string> RecognizeAsync(Bitmap bitmap, IReadOnlyList<string> languageTags)
    {
        if (languageTags == null || languageTags.Count == 0) languageTags = new[] { "zh-Hans" };

        using var prepared = Prepare(bitmap);
        byte[] bytes;
        using (var memory = new MemoryStream())
        {
            prepared.Save(memory, ImageFormat.Png);
            bytes = memory.ToArray();
        }

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        OcrEngine? engine = null;
        foreach (var languageTag in languageTags)
        {
            try
            {
                engine = OcrEngine.TryCreateFromLanguage(new Language(languageTag));
            }
            catch
            {
                engine = null;
            }
            if (engine != null) break;
        }
        engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
        {
            throw new InvalidOperationException(
                "系统未安装可用的 OCR 语言包。请打开 Windows 设置 → 时间和语言 → 语言和区域，添加所需语言，并在语言选项中安装“光学字符识别”。");
        }

        var result = await engine.RecognizeAsync(softwareBitmap);
        var lines = result.Lines
            .Select(line => Normalize(line.Text.Trim()))
            .Where(text => text.Length > 0)
            .ToList();

        if (lines.Count == 0
            && languageTags.Any(tag => tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            && !engine.RecognizerLanguage.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "未能识别文字：可能缺少中文 OCR 语言包。请在 Windows 设置 → 时间和语言 → 语言和区域 → 中文 → 语言选项中安装“光学字符识别”。");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Normalize(string line)
    {
        if (line.Length == 0) return line;

        var builder = new StringBuilder(line.Length);
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == ' ' || c == '\u3000')
            {
                var previous = i > 0 ? line[i - 1] : '\0';
                var next = i + 1 < line.Length ? line[i + 1] : '\0';
                if (IsCjk(previous) && IsCjk(next)) continue;
            }
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static bool IsCjk(char c)
        => (c >= 0x4E00 && c <= 0x9FFF)
           || (c >= 0x3400 && c <= 0x4DBF)
           || (c >= 0xF900 && c <= 0xFAFF)
           || (c >= 0x3040 && c <= 0x30FF)
           || (c >= 0x3000 && c <= 0x303F)
           || (c >= 0xFF00 && c <= 0xFF65);

    private static Bitmap Prepare(Bitmap source)
    {
        var maxDimension = (int)OcrEngine.MaxImageDimension;
        var maxSide = Math.Max(source.Width, source.Height);
        var scale = maxSide < 900 ? 2.0 : maxSide < 1600 ? 1.5 : 1.0;

        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var fit = Math.Min(1.0, Math.Min((double)maxDimension / width, (double)maxDimension / height));
        if (fit < 1.0)
        {
            width = Math.Max(1, (int)(width * fit));
            height = Math.Max(1, (int)(height * fit));
        }

        if (width == source.Width && height == source.Height) return new Bitmap(source);

        var target = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(target);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        return target;
    }
}
