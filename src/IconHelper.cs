using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace GhostDesk
{
    internal static class IconHelper
    {
        private static readonly Dictionary<string, BitmapSource> Cache =
            new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

        public static BitmapSource GetIcon(DesktopItem item)
        {
            string key = (item.Type ?? "") + "|" + (item.Path ?? "") + "|" + (item.IconPath ?? "");
            BitmapSource cached;
            if (Cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            BitmapSource result = ExtractIcon(item);
            if (result != null && result.CanFreeze)
            {
                result.Freeze();
            }
            if (result != null)
            {
                Cache[key] = result;
            }
            return result;
        }

        private static BitmapSource ExtractIcon(DesktopItem item)
        {
            string path = item.Path;
            if (!string.IsNullOrEmpty(item.IconPath))
            {
                BitmapSource customIcon = LoadCustomIcon(item.IconPath);
                if (customIcon != null)
                {
                    return customIcon;
                }
            }

            if (string.IsNullOrEmpty(path) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                BitmapSource shellIcon = ExtractByShellItem(ShellUriToParsingName(path), 48, 0);
                if (shellIcon != null)
                {
                    return shellIcon;
                }

                return FromIconHandle(SystemIcons.Application.Handle) ??
                       FromBitmap(SystemIcons.Application.ToBitmap());
            }

            try
            {
                BitmapSource imageThumbnail = LoadImageThumbnail(path);
                if (imageThumbnail != null)
                {
                    return imageThumbnail;
                }

                string extension = Path.GetExtension(path).ToLowerInvariant();
                bool isImageFile = extension == ".jpg" || extension == ".jpeg" ||
                    extension == ".png" || extension == ".bmp" || extension == ".gif" ||
                    extension == ".tif" || extension == ".tiff";
                int shellOptions = isImageFile ? 0 : NativeMethods.SIIGBF_ICONONLY;
                BitmapSource shellItemIcon = ExtractByShellItem(path, 48, shellOptions);
                if (shellItemIcon != null)
                {
                    return shellItemIcon;
                }

                NativeMethods.SHFILEINFO info = new NativeMethods.SHFILEINFO();
                uint flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON;
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;
                }

                IntPtr hIcon = NativeMethods.SHGetFileInfo(
                    path,
                    0,
                    ref info,
                    (uint)Marshal.SizeOf(typeof(NativeMethods.SHFILEINFO)),
                    flags);

                if (hIcon != IntPtr.Zero)
                {
                    try
                    {
                        return FromIconHandle(hIcon);
                    }
                    finally
                    {
                        NativeMethods.DestroyIcon(hIcon);
                    }
                }
            }
            catch
            {
            }

            return FromIconHandle(SystemIcons.Application.Handle) ??
                   FromBitmap(SystemIcons.Application.ToBitmap());
        }

        private static BitmapSource LoadCustomIcon(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".exe" || extension == ".dll")
            {
                return ExtractByShellItem(path, 48, NativeMethods.SIIGBF_ICONONLY);
            }

            try
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 96;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                if (image.CanFreeze)
                {
                    image.Freeze();
                }

                return image;
            }
            catch
            {
                return null;
            }
        }

        private static BitmapSource LoadImageThumbnail(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png" &&
                extension != ".bmp" && extension != ".gif" && extension != ".tif" && extension != ".tiff")
            {
                return null;
            }

            try
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 96;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                if (image.CanFreeze)
                {
                    image.Freeze();
                }

                return image;
            }
            catch
            {
                return null;
            }
        }

        private static BitmapSource ExtractByShellItem(string path, int size, int options)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            try
            {
                Guid factoryGuid = new Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B");
                NativeMethods.IShellItemImageFactory factory;
                NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, ref factoryGuid, out factory);

                IntPtr hBitmap;
                factory.GetImage(
                    new NativeMethods.SIZE(size, size),
                    NativeMethods.SIIGBF_BIGGERSIZEOK | options,
                    out hBitmap);

                try
                {
                    if (hBitmap == IntPtr.Zero)
                    {
                        return null;
                    }

                    BitmapSource result = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    if (result.CanFreeze)
                    {
                        result.Freeze();
                    }

                    return result;
                }
                finally
                {
                    NativeMethods.DeleteObject(hBitmap);
                }
            }
            catch
            {
                return null;
            }
        }

        private static string ShellUriToParsingName(string path)
        {
            if (string.Equals(path, "shell:MyComputerFolder", StringComparison.OrdinalIgnoreCase))
            {
                return "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
            }
            if (string.Equals(path, "shell:ControlPanelFolder", StringComparison.OrdinalIgnoreCase))
            {
                return "shell:::{21EC2020-3AEA-1069-A2DD-08002B30309D}";
            }
            if (string.Equals(path, "shell:RecycleBinFolder", StringComparison.OrdinalIgnoreCase))
            {
                return "shell:::{645FF040-5081-101B-9F08-00AA002F954E}";
            }

            return null;
        }

        private static BitmapSource FromIconHandle(IntPtr hIcon)
        {
            if (hIcon == IntPtr.Zero)
            {
                return null;
            }

            BitmapSource result = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            if (result.CanFreeze)
            {
                result.Freeze();
            }

            return result;
        }

        private static BitmapSource FromBitmap(Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(48, 48));
            }
            finally
            {
                NativeMethods.DeleteObject(hBitmap);
            }
        }
    }
}
