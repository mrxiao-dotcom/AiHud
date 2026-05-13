using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace AiHUD.Models;

public static class IconExtractor
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static BitmapImage? ExtractIcon(string exePath)
    {
        try
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                System.Diagnostics.Debug.WriteLine($"[IconExtractor.ExtractIcon] 文件不存在: {exePath}");
                return null;
            }

            System.Diagnostics.Debug.WriteLine($"[IconExtractor.ExtractIcon] 开始提取: {exePath}");

            IntPtr iconHandle = ExtractIcon(IntPtr.Zero, exePath, 0);
            if (iconHandle == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine($"[IconExtractor.ExtractIcon] 无法获取图标句柄");
                return null;
            }

            try
            {
                var icon = System.Drawing.Icon.FromHandle(iconHandle);
                var bitmapImage = IconToBitmapImage(icon);
                System.Diagnostics.Debug.WriteLine($"[IconExtractor.ExtractIcon] 成功: {bitmapImage != null}");
                return bitmapImage;
            }
            finally
            {
                DestroyIcon(iconHandle);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IconExtractor.ExtractIcon] 异常: {ex.Message}");
            return null;
        }
    }

    public static BitmapImage? ExtractIconFromPath(string iconPath)
    {
        return ExtractIcon(iconPath);
    }

    private static BitmapImage? IconToBitmapImage(System.Drawing.Icon icon)
    {
        try
        {
            using var bitmap = icon.ToBitmap();
            using var memoryStream = new MemoryStream();
            bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
            memoryStream.Position = 0;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = memoryStream;
            bitmapImage.EndInit();
            bitmapImage.Freeze(); // 关键：使图片可在任何线程访问

            return bitmapImage;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IconExtractor.IconToBitmapImage] 异常: {ex.Message}");
            return null;
        }
    }

    public static BitmapImage? ExtractIconFromFile(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"[IconExtractor.ExtractIconFromFile] 文件不存在: {filePath}");
                return null;
            }

            System.Diagnostics.Debug.WriteLine($"[IconExtractor.ExtractIconFromFile] 开始: {filePath}");

            var icon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
            if (icon == null)
            {
                System.Diagnostics.Debug.WriteLine($"[IconExtractor.ExtractIconFromFile] 无法提取关联图标");
                return null;
            }

            var bitmapImage = IconToBitmapImage(icon);
            System.Diagnostics.Debug.WriteLine($"[IconExtractor.ExtractIconFromFile] 成功: {bitmapImage != null}");
            return bitmapImage;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IconExtractor.ExtractIconFromFile] 异常: {ex.Message}");
            return null;
        }
    }

    public static BitmapImage? LoadBitmapFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmapImage.DecodePixelWidth = 64;
            bitmapImage.EndInit();
            bitmapImage.Freeze(); // 关键：使图片可在任何线程访问

            return bitmapImage;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IconExtractor.LoadBitmapFromFile] 异常: {ex.Message}");
            return null;
        }
    }

    public static BitmapImage? LoadIconFile(string iconPath)
    {
        try
        {
            if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
            {
                System.Diagnostics.Debug.WriteLine($"[IconExtractor.LoadIconFile] 文件不存在: {iconPath}");
                return null;
            }

            var extension = Path.GetExtension(iconPath).ToLowerInvariant();
            System.Diagnostics.Debug.WriteLine($"[IconExtractor.LoadIconFile] 类型: {extension}");

            BitmapImage? result = null;

            if (extension == ".ico")
            {
                // 使用更好的方式加载 .ico 文件
                result = LoadIcoFile(iconPath);
            }
            else
            {
                result = LoadBitmapFromFile(iconPath);
            }

            System.Diagnostics.Debug.WriteLine($"[IconExtractor.LoadIconFile] 结果: {result != null}");
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IconExtractor.LoadIconFile] 异常: {ex.Message}");
            return null;
        }
    }

    private static BitmapImage? LoadIcoFile(string icoPath)
    {
        try
        {
            // 读取 .ico 文件并转换为 BitmapImage
            // .ico 文件格式: https://en.wikipedia.org/wiki/ICO_(file_format)
            using var fileStream = new FileStream(icoPath, FileMode.Open, FileAccess.Read);
            using var binaryReader = new BinaryReader(fileStream);

            // 读取 ICO 头
            short reserved = binaryReader.ReadInt16();
            short type = binaryReader.ReadInt16();
            short count = binaryReader.ReadInt16();

            if (reserved != 0 || type != 1 || count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[IconExtractor.LoadIcoFile] 无效的 ICO 文件格式");
                return null;
            }

            // 读取第一个图标目录项
            byte width = binaryReader.ReadByte();
            byte height = binaryReader.ReadByte();
            byte colorCount = binaryReader.ReadByte();
            byte reserved2 = binaryReader.ReadByte();
            short planes = binaryReader.ReadInt16();
            short bitCount = binaryReader.ReadInt16();
            int dataSize = binaryReader.ReadInt32();
            int dataOffset = binaryReader.ReadInt32();

            // 移动到图像数据位置
            fileStream.Seek(dataOffset, SeekOrigin.Begin);

            // 读取 PNG 或 BMP 数据
            var imageData = binaryReader.ReadBytes(dataSize);

            // 检查是否是 PNG 格式（大多数现代 .ico 文件使用 PNG）
            if (imageData.Length > 8)
            {
                // PNG 签名: 89 50 4E 47 0D 0A 1A 0A
                bool isPng = imageData[0] == 0x89 && imageData[1] == 0x50 &&
                             imageData[2] == 0x4E && imageData[3] == 0x47;

                if (isPng)
                {
                    using var memoryStream = new MemoryStream(imageData);
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = memoryStream;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                    return bitmapImage;
                }
            }

            // 如果不是 PNG，使用 System.Drawing.Icon 作为后备方案
            fileStream.Seek(dataOffset, SeekOrigin.Begin);
            using var icon = new System.Drawing.Icon(fileStream);
            return IconToBitmapImage(icon);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IconExtractor.LoadIcoFile] 异常: {ex.Message}");

            // 最后的后备方案：直接用路径构造 Icon
            try
            {
                using var icon = new System.Drawing.Icon(icoPath);
                return IconToBitmapImage(icon);
            }
            catch
            {
                return null;
            }
        }
    }
}
