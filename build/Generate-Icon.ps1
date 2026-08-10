[CmdletBinding()]
param(
    [string]$OutputPath
)

# Draws src\App\MacBookEco.ico.
#
# The application used to ship SystemIcons.Information, which put the same blue
# circled "i" in the title bar, the taskbar and the notification area. For a
# utility that lives in the tray that is a real problem: the icon is
# indistinguishable from a system notification.
#
# The mark is deliberately plain, because it has to survive being drawn at
# 16x16: a rounded square in the dashboard accent colour with a white leaf.
# Nothing here is clever, and that is the point - at tray size, detail is noise.
#
# The drawing and the ICO container are written in C# rather than PowerShell
# because PowerShell unrolls a byte[] returned from a function, which silently
# produces an icon with no image data.
#
# Regenerate with:  .\build\Generate-Icon.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path (Split-Path -Parent $PSScriptRoot) "src\App\MacBookEco.ico"
}

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies @("System.Drawing.dll") -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class MacBookEcoIconWriter
{
    // DashboardTheme.AccentColor, so the icon and the UI agree.
    private static readonly Color Accent = Color.FromArgb(255, 38, 111, 105);
    private static readonly Color AccentLight = Color.FromArgb(255, 58, 150, 140);

    // Stops at 128. A 256px DIB entry costs a quarter of a megabyte on its own,
    // and System.Drawing on .NET Framework does not reliably select it anyway;
    // Windows scales 128 up for the one Explorer view that wants more.
    private static readonly int[] Sizes = { 16, 20, 24, 32, 48, 64, 128 };

    public static void Write(string path)
    {
        byte[][] images = new byte[Sizes.Length][];
        for (int i = 0; i < Sizes.Length; i++)
        {
            using (Bitmap bitmap = Draw(Sizes[i]))
            {
                images[i] = ToDib(bitmap);
            }
        }

        using (FileStream file = File.Create(path))
        using (BinaryWriter writer = new BinaryWriter(file))
        {
            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)Sizes.Length);

            int offset = 6 + (16 * Sizes.Length);
            for (int i = 0; i < Sizes.Length; i++)
            {
                // 0 encodes 256 in a single byte.
                byte dimension = Sizes[i] >= 256 ? (byte)0 : (byte)Sizes[i];
                writer.Write(dimension);
                writer.Write(dimension);
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((short)1);
                writer.Write((short)32);
                writer.Write(images[i].Length);
                writer.Write(offset);
                offset += images[i].Length;
            }

            for (int i = 0; i < Sizes.Length; i++)
            {
                writer.Write(images[i]);
            }
        }
    }

    private static Bitmap Draw(int size)
    {
        Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float s = size;
            // Full bleed at tray sizes; a visible corner radius only once there
            // are enough pixels for it to read as a shape rather than fringing.
            float inset = size <= 20 ? 0f : s * 0.02f;
            RectangleF rect = new RectangleF(inset, inset, s - 2 * inset, s - 2 * inset);
            float d = s * 0.22f * 2f;

            using (GraphicsPath plate = new GraphicsPath())
            {
                plate.AddArc(rect.X, rect.Y, d, d, 180, 90);
                plate.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                plate.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                plate.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                plate.CloseFigure();
                using (LinearGradientBrush brush =
                    new LinearGradientBrush(rect, AccentLight, Accent, 45f))
                {
                    g.FillPath(brush, plate);
                }
            }

            // A leaf: two mirrored curves meeting at opposite corners.
            float cx = s * 0.5f;
            float cy = s * 0.5f;
            float r = s * 0.30f;
            float tipX = cx + r * 0.72f;
            float tipY = cy - r * 0.72f;
            float baseX = cx - r * 0.72f;
            float baseY = cy + r * 0.72f;
            float bend = r * 0.95f;

            using (GraphicsPath leaf = new GraphicsPath())
            {
                leaf.AddBezier(
                    baseX, baseY,
                    baseX, baseY - bend,
                    tipX - bend, tipY,
                    tipX, tipY);
                leaf.AddBezier(
                    tipX, tipY,
                    tipX, tipY + bend,
                    baseX + bend, baseY,
                    baseX, baseY);
                leaf.CloseFigure();
                using (SolidBrush white = new SolidBrush(Color.White))
                {
                    g.FillPath(white, leaf);
                }
            }

            // The midrib only survives above tray size. Below that it muddies
            // the silhouette, so the leaf stays a solid shape.
            if (size >= 32)
            {
                using (Pen pen = new Pen(Accent, Math.Max(1f, s * 0.045f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(
                        pen,
                        baseX + s * 0.03f, baseY - s * 0.03f,
                        tipX - s * 0.03f, tipY + s * 0.03f);
                }
            }
        }

        return bitmap;
    }

    // 32bpp DIB entries rather than PNG entries: every consumer, including
    // System.Drawing.Icon on .NET Framework, handles DIB without surprises.
    private static byte[] ToDib(Bitmap bitmap)
    {
        int w = bitmap.Width;
        int h = bitmap.Height;
        int maskRowBytes = ((w + 31) / 32) * 4;

        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            // BITMAPINFOHEADER. Height is doubled: XOR image plus AND mask.
            writer.Write(40);
            writer.Write(w);
            writer.Write(h * 2);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(0);
            writer.Write((w * h * 4) + (maskRowBytes * h));
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);

            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[w * 4];
                // Bottom-up.
                for (int y = h - 1; y >= 0; y--)
                {
                    IntPtr line = new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride);
                    System.Runtime.InteropServices.Marshal.Copy(line, row, 0, row.Length);
                    writer.Write(row);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            // Alpha already carries transparency, so the AND mask is all
            // zeroes, but the rows must still be padded to four bytes.
            byte[] zeroRow = new byte[maskRowBytes];
            for (int y = 0; y < h; y++)
            {
                writer.Write(zeroRow);
            }

            writer.Flush();
            return stream.ToArray();
        }
    }
}
'@

[MacBookEcoIconWriter]::Write($OutputPath)

$info = Get-Item $OutputPath
Write-Host ("Wrote {0} ({1:N0} bytes)" -f $OutputPath, $info.Length)

# Prove the result is loadable at the sizes Windows will actually ask for.
$icon = New-Object Drawing.Icon($OutputPath)
try {
    foreach ($size in @(16, 32, 128)) {
        $sized = New-Object Drawing.Icon($icon, (New-Object Drawing.Size($size, $size)))
        try {
            $bitmap = $sized.ToBitmap()
            try {
                $centre = $bitmap.GetPixel([int]($bitmap.Width / 2), [int]($bitmap.Height / 2))
                Write-Host ("  {0,3}px -> {1}x{2}, centre #{3:X2}{4:X2}{5:X2}, alpha {6}" -f `
                    $size, $bitmap.Width, $bitmap.Height, $centre.R, $centre.G, $centre.B, $centre.A)
            }
            finally {
                $bitmap.Dispose()
            }
        }
        finally {
            $sized.Dispose()
        }
    }
}
finally {
    $icon.Dispose()
}
