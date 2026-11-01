using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

// font-ocr.exe <line.png> <ocrText> [fontPath]
// 固定字体逐字对齐：用字体度量算 advance，不用等分整行。
class FontOcr
{
    static PrivateFontCollection PFC;
    static FontFamily FAM;
    static string FontPath;
    static Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 2) { Console.Error.WriteLine("usage: font-ocr.exe <line.png> <ocrText> [fontPath]"); Environment.Exit(2); }
        FontPath = args.Length > 2 ? args[2] : @"D:\BACKROOMS\客户端\fonts\NotoSansCJKtc-Regular.otf";
        LoadFont(FontPath);
        var line = TextMask(args[0]);
        var res = Match(line, args[1]);
        Console.WriteLine(new JavaScriptSerializer().Serialize(res));
    }

    static void LoadFont(string path)
    {
        PFC = new PrivateFontCollection();
        try { PFC.AddFontFile(path); } catch { }
        FAM = PFC.Families.Length > 0 ? PFC.Families[0] : new FontFamily("Microsoft JhengHei");
    }

    static Bitmap TextMask(string path)
    {
        using (var src = new Bitmap(path))
        {
            int min = 255, max = 0;
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    var p = src.GetPixel(x, y);
                    int lum = (p.R * 299 + p.G * 587 + p.B * 114) / 1000;
                    if (lum < min) min = lum;
                    if (lum > max) max = lum;
                }
            long bs = 0; int bn = 0;
            for (int x = 0; x < src.Width; x++) { var a = src.GetPixel(x, 0); var z = src.GetPixel(x, src.Height - 1); bs += (a.R + a.G + a.B) / 3 + (z.R + z.G + z.B) / 3; bn += 2; }
            for (int y = 0; y < src.Height; y++) { var a = src.GetPixel(0, y); var z = src.GetPixel(src.Width - 1, y); bs += (a.R + a.G + a.B) / 3 + (z.R + z.G + z.B) / 3; bn += 2; }
            int bmean = bn > 0 ? (int)(bs / bn) : 0;
            bool darkText = bmean > 128;
            int thr = min + (max - min) * 45 / 100;
            var b = new Bitmap(src.Width, src.Height);
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    var p = src.GetPixel(x, y);
                    int lum = (p.R * 299 + p.G * 587 + p.B * 114) / 1000;
                    bool on = darkText ? (lum < thr) : (lum > thr);
                    b.SetPixel(x, y, on ? Color.White : Color.Black);
                }
            return b;
        }
    }

    static Rectangle Bounds(Bitmap b)
    {
        int x0 = b.Width, y0 = b.Height, x1 = -1, y1 = -1;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (b.GetPixel(x, y).R > 127) { if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y; }
        if (x1 < x0) return Rectangle.Empty;
        return new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    static Bitmap Crop(Bitmap b, Rectangle r)
    {
        var o = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height));
        using (var g = Graphics.FromImage(o)) { g.DrawImage(b, new Rectangle(0, 0, o.Width, o.Height), r, GraphicsUnit.Pixel); }
        return o;
    }

    static Bitmap Render(char c, int px)
    {
        string key = c + "|" + px;
        if (Cache.ContainsKey(key)) return Cache[key];
        var b = new Bitmap(px * 2 + 4, px * 2 + 4);
        using (var g = Graphics.FromImage(b))
        {
            g.Clear(Color.Black);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using (var f = new Font(FAM, px, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(Color.White))
                g.DrawString(c.ToString(), f, br, 1, 1, StringFormat.GenericTypographic);
        }
        var bb = Bounds(b);
        var t = bb.IsEmpty ? b : Crop(b, bb);
        Cache[key] = t;
        return t;
    }

    static Bitmap Normalize(Bitmap src, int w, int h)
    {
        var b = new Bitmap(w, h);
        using (var g = Graphics.FromImage(b))
        {
            g.Clear(Color.Black);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);
        }
        return b;
    }

    static double Score(Bitmap a, Bitmap b)
    {
        int hit = 0, uni = 0;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                bool pa = a.GetPixel(x, y).R > 127, pb = b.GetPixel(x, y).R > 127;
                if (pa || pb) uni++;
                if (pa && pb) hit++;
            }
        return uni == 0 ? 0 : (double)hit / uni;
    }

    static double Advance(char c, int px)
    {
        using (var b = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(b))
        using (var f = new Font(FAM, px, FontStyle.Regular, GraphicsUnit.Pixel))
            return g.MeasureString(c.ToString(), f, int.MaxValue, StringFormat.GenericTypographic).Width;
    }

    static object Match(Bitmap src, string ocr)
    {
        var box = Bounds(src);
        if (box.IsEmpty) return new { text = ocr, error = "blank" };
        var line = Crop(src, box);
        var chars = ocr.Where(c => !char.IsWhiteSpace(c)).ToArray();
        if (chars.Length == 0) return new { text = ocr, error = "empty" };

        // 找最合适的 font size：實際字寬總和最接近行寬
        int bestSize = 24; double bestErr = double.MaxValue;
        for (int px = 14; px <= 40; px++)
        {
            double w = chars.Sum(c => Advance(c, px));
            double err = Math.Abs(w - line.Width);
            if (err < bestErr) { bestErr = err; bestSize = px; }
        }
        double total = chars.Sum(c => Advance(c, bestSize));
        double scale = total > 0 ? (double)line.Width / total : 1;
        var outp = new StringBuilder();
        var details = new List<object>();
        double xpos = 0;
        for (int i = 0; i < chars.Length; i++)
        {
            double adv = Math.Max(4, Advance(chars[i], bestSize) * scale);
            int x0 = (int)Math.Round(xpos), x1 = (int)Math.Round(xpos + adv);
            x0 = Math.Max(0, Math.Min(line.Width - 1, x0));
            x1 = Math.Max(x0 + 1, Math.Min(line.Width, x1));
            var cell = Crop(line, new Rectangle(x0, 0, x1 - x0, line.Height));
            var nb = Normalize(cell, 32, 32);
            char best = chars[i]; double bestScore = 0;
            foreach (var cand in Candidates(chars[i]))
            {
                var t = Normalize(Render(cand, bestSize), 32, 32);
                double s = Score(nb, t);
                if (s > bestScore) { bestScore = s; best = cand; }
            }
            outp.Append(best);
            details.Add(new { ocr = chars[i], best = best, score = Math.Round(bestScore, 3), x = x0, w = x1 - x0 });
            xpos += adv;
        }
        return new { text = outp.ToString(), size = bestSize, scale = Math.Round(scale, 3), details = details };
    }

    static List<char> Candidates(char c)
    {
        var map = new Dictionary<char, string> {
            {'夭',"天"}, {'天',"夭"}, {'潦',"流"}, {'流',"潦"}, {'冂',"门門"}, {'门',"門"}, {'門',"门"},
            {'囗',"口"}, {'口',"囗"}, {'麵',"面"}, {'面',"麵"}, {'迴',"回"}, {'回',"迴"},
            {'祂',"他"}, {'牠',"它"}, {'祇',"只"}, {'齣',"出"}, {'證',"证"}, {'証',"证"},
            {'綫',"袋线線"}, {'迄',"追"}, {'寰',"查"}, {'蟋',"爬"}, {'隻',"只"}, {'妳',"你"}, {'你',"妳"}
        };
        var list = new List<char> { c };
        if (map.ContainsKey(c)) foreach (var x in map[c]) if (!list.Contains(x)) list.Add(x);
        return list;
    }
}
