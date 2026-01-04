using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace BACTBiometricClient.Helpers
{
    /// <summary>
    /// Helper class for image conversion operations
    /// </summary>
    public static class ImageHelper
    {
        /// <summary>
        /// Convert byte array to BitmapImage for display in WPF
        /// </summary>
        public static BitmapImage ByteArrayToBitmapImage(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0)
                return null;

            try
            {
                var bitmap = new BitmapImage();
                using (var stream = new MemoryStream(imageData))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze(); // Important for cross-thread access
                }
                return bitmap;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to convert byte array to BitmapImage", ex);
                return null;
            }
        }

        /// <summary>
        /// Convert BitmapImage to byte array
        /// </summary>
        public static byte[] BitmapImageToByteArray(BitmapImage bitmap)
        {
            if (bitmap == null)
                return null;

            try
            {
                var encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using var stream = new MemoryStream();
                encoder.Save(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to convert BitmapImage to byte array", ex);
                return null;
            }
        }

        /// <summary>
        /// Convert byte array to Base64 string
        /// </summary>
        public static string ByteArrayToBase64(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0)
                return null;

            try
            {
                return Convert.ToBase64String(imageData);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to convert byte array to base64", ex);
                return null;
            }
        }

        /// <summary>
        /// Convert Base64 string to byte array
        /// </summary>
        public static byte[] Base64ToByteArray(string base64String)
        {
            if (string.IsNullOrWhiteSpace(base64String))
                return null;

            try
            {
                return Convert.FromBase64String(base64String);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to convert base64 to byte array", ex);
                return null;
            }
        }

        /// <summary>
        /// Load image from file path
        /// </summary>
        public static BitmapImage LoadImageFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(filePath);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load image from file: {filePath}", ex);
                return null;
            }
        }

        /// <summary>
        /// Save byte array to file
        /// </summary>
        public static bool SaveByteArrayToFile(byte[] imageData, string filePath)
        {
            if (imageData == null || imageData.Length == 0 || string.IsNullOrWhiteSpace(filePath))
                return false;

            try
            {
                File.WriteAllBytes(filePath, imageData);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save image to file: {filePath}", ex);
                return false;
            }
        }

        /// <summary>
        /// Resize image to specified dimensions (maintains aspect ratio)
        /// </summary>
        public static BitmapImage ResizeImage(BitmapImage source, int maxWidth, int maxHeight)
        {
            if (source == null)
                return null;

            try
            {
                double scale = Math.Min((double)maxWidth / source.PixelWidth, (double)maxHeight / source.PixelHeight);

                if (scale >= 1)
                    return source; // No need to resize

                int newWidth = (int)(source.PixelWidth * scale);
                int newHeight = (int)(source.PixelHeight * scale);

                var resized = new TransformedBitmap(source, new System.Windows.Media.ScaleTransform(scale, scale));

                var bitmap = new BitmapImage();
                var encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(resized));

                using var stream = new MemoryStream();
                encoder.Save(stream);
                stream.Position = 0;

                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to resize image", ex);
                return source;
            }
        }

        /// <summary>
        /// Create a placeholder image with text
        /// </summary>
        public static BitmapImage CreatePlaceholderImage(string text, int width = 200, int height = 200)
        {
            try
            {
                var visual = new System.Windows.Controls.Canvas
                {
                    Width = width,
                    Height = height,
                    Background = System.Windows.Media.Brushes.LightGray
                };

                var textBlock = new System.Windows.Controls.TextBlock
                {
                    Text = text,
                    FontSize = 16,
                    Foreground = System.Windows.Media.Brushes.DarkGray,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };

                visual.Children.Add(textBlock);

                var renderBitmap = new RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                visual.Measure(new System.Windows.Size(width, height));
                visual.Arrange(new System.Windows.Rect(new System.Windows.Size(width, height)));
                renderBitmap.Render(visual);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                using var stream = new MemoryStream();
                encoder.Save(stream);
                stream.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to create placeholder image", ex);
                return null;
            }
        }
    }
}