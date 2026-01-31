using System;
using System.IO;
using System.Windows.Media.Imaging;
using BACTBiometricClient.Helpers;

namespace BACTBiometricClient.Services
{
    /// <summary>
    /// Comprehensive fingerprint processing and format conversion utilities
    /// Implements Requirements 6.4 and 7.2 for fingerprint data processing
    /// </summary>
    public static class FingerprintProcessor
    {
        #region Quality Validation Constants
        
        public const int MINIMUM_QUALITY_THRESHOLD = 60;
        public const int GOOD_QUALITY_THRESHOLD = 75;
        public const int EXCELLENT_QUALITY_THRESHOLD = 85;
        
        #endregion

        #region Template Processing

        /// <summary>
        /// Convert fingerprint template to Base64 string for storage and transmission
        /// Requirement 6.4: Convert templates to Base64 format for storage
        /// </summary>
        /// <param name="template">Raw fingerprint template bytes</param>
        /// <returns>Base64 encoded template string</returns>
        public static string TemplateToBase64(byte[] template)
        {
            if (template == null || template.Length == 0)
            {
                throw new ArgumentException("Template data cannot be null or empty", nameof(template));
            }

            try
            {
                return Convert.ToBase64String(template);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to convert template to Base64: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Convert Base64 string back to fingerprint template bytes
        /// </summary>
        /// <param name="base64Template">Base64 encoded template string</param>
        /// <returns>Raw fingerprint template bytes</returns>
        public static byte[] Base64ToTemplate(string base64Template)
        {
            if (string.IsNullOrWhiteSpace(base64Template))
            {
                throw new ArgumentException("Base64 template string cannot be null or empty", nameof(base64Template));
            }

            try
            {
                return Convert.FromBase64String(base64Template);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to convert Base64 to template: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Validate fingerprint template integrity
        /// </summary>
        /// <param name="template">Fingerprint template to validate</param>
        /// <returns>True if template is valid</returns>
        public static bool ValidateTemplate(byte[] template)
        {
            if (template == null || template.Length == 0)
                return false;

            // Basic validation - SecuGen templates are typically 400 bytes
            if (template.Length < 100 || template.Length > 1000)
                return false;

            // Check if template contains meaningful data (not all zeros)
            int nonZeroCount = 0;
            for (int i = 0; i < Math.Min(template.Length, 100); i++)
            {
                if (template[i] != 0) nonZeroCount++;
            }

            // Template should have at least 50% non-zero bytes in first 100 bytes
            return nonZeroCount >= 50;
        }

        #endregion

        #region Image Processing

        /// <summary>
        /// Convert fingerprint image to Base64 string for storage and transmission
        /// Requirement 6.4: Convert images to Base64 format for storage
        /// </summary>
        /// <param name="imageData">Raw fingerprint image bytes</param>
        /// <returns>Base64 encoded image string</returns>
        public static string ImageToBase64(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0)
            {
                throw new ArgumentException("Image data cannot be null or empty", nameof(imageData));
            }

            try
            {
                return Convert.ToBase64String(imageData);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to convert image to Base64: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Convert Base64 string back to fingerprint image bytes
        /// </summary>
        /// <param name="base64Image">Base64 encoded image string</param>
        /// <returns>Raw fingerprint image bytes</returns>
        public static byte[] Base64ToImage(string base64Image)
        {
            if (string.IsNullOrWhiteSpace(base64Image))
            {
                throw new ArgumentException("Base64 image string cannot be null or empty", nameof(base64Image));
            }

            try
            {
                return Convert.FromBase64String(base64Image);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to convert Base64 to image: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Convert raw fingerprint image data to PNG format for better storage
        /// </summary>
        /// <param name="rawImageData">Raw fingerprint image bytes</param>
        /// <param name="width">Image width in pixels</param>
        /// <param name="height">Image height in pixels</param>
        /// <returns>PNG formatted image bytes</returns>
        public static byte[] ConvertToPng(byte[] rawImageData, int width, int height)
        {
            if (rawImageData == null || rawImageData.Length == 0)
            {
                throw new ArgumentException("Raw image data cannot be null or empty", nameof(rawImageData));
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("Width and height must be positive values");
            }

            try
            {
                // Create bitmap from raw grayscale data
                var bitmap = BitmapSource.Create(
                    width, height, 96, 96,
                    System.Windows.Media.PixelFormats.Gray8,
                    null, rawImageData, width);

                // Convert to PNG
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using var stream = new MemoryStream();
                encoder.Save(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to convert to PNG: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Create a BitmapImage from fingerprint image data for UI display
        /// </summary>
        /// <param name="imageData">Fingerprint image bytes</param>
        /// <returns>BitmapImage for WPF display</returns>
        public static BitmapImage CreateDisplayImage(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0)
                return null;

            return ImageHelper.ByteArrayToBitmapImage(imageData);
        }

        #endregion

        #region Quality Validation

        /// <summary>
        /// Validate fingerprint quality score and provide feedback
        /// Requirement 7.2: Validate quality scores and prompt for recapture if needed
        /// </summary>
        /// <param name="qualityScore">Quality score from scanner (0-100)</param>
        /// <returns>Quality validation result</returns>
        public static QualityValidationResult ValidateQuality(int qualityScore)
        {
            var result = new QualityValidationResult
            {
                Score = qualityScore,
                IsAcceptable = qualityScore >= MINIMUM_QUALITY_THRESHOLD
            };

            if (qualityScore >= EXCELLENT_QUALITY_THRESHOLD)
            {
                result.Level = QualityLevel.Excellent;
                result.Message = $"Excellent quality ({qualityScore}%) - Perfect for registration";
                result.Recommendation = "Proceed with registration";
            }
            else if (qualityScore >= GOOD_QUALITY_THRESHOLD)
            {
                result.Level = QualityLevel.Good;
                result.Message = $"Good quality ({qualityScore}%) - Suitable for registration";
                result.Recommendation = "Proceed with registration";
            }
            else if (qualityScore >= MINIMUM_QUALITY_THRESHOLD)
            {
                result.Level = QualityLevel.Fair;
                result.Message = $"Fair quality ({qualityScore}%) - Acceptable but could be better";
                result.Recommendation = "Consider recapturing for better quality";
            }
            else
            {
                result.Level = QualityLevel.Poor;
                result.Message = $"Poor quality ({qualityScore}%) - Below minimum threshold";
                result.Recommendation = "Recapture required - Clean finger and scanner, press firmly";
            }

            return result;
        }

        /// <summary>
        /// Get detailed quality guidance based on score
        /// </summary>
        /// <param name="qualityScore">Quality score from scanner</param>
        /// <returns>Detailed guidance message</returns>
        public static string GetQualityGuidance(int qualityScore)
        {
            return qualityScore switch
            {
                >= EXCELLENT_QUALITY_THRESHOLD => "Perfect! The fingerprint quality is excellent.",
                >= GOOD_QUALITY_THRESHOLD => "Good quality. The fingerprint is clear and suitable for matching.",
                >= MINIMUM_QUALITY_THRESHOLD => "Acceptable quality, but consider recapturing for better results.",
                >= 40 => "Poor quality. Please clean your finger and the scanner, then press firmly.",
                >= 20 => "Very poor quality. Ensure finger is dry, clean the scanner surface, and center your finger.",
                _ => "Extremely poor quality. Check scanner connection and ensure proper finger placement."
            };
        }

        #endregion

        #region Data Validation

        /// <summary>
        /// Validate complete fingerprint data package
        /// </summary>
        /// <param name="template">Fingerprint template</param>
        /// <param name="imageData">Fingerprint image</param>
        /// <param name="qualityScore">Quality score</param>
        /// <returns>Validation result</returns>
        public static FingerprintValidationResult ValidateFingerprintData(
            byte[] template, byte[] imageData, int qualityScore)
        {
            var result = new FingerprintValidationResult();

            // Validate template
            if (!ValidateTemplate(template))
            {
                result.IsValid = false;
                result.Errors.Add("Invalid fingerprint template data");
            }

            // Validate image
            if (imageData == null || imageData.Length == 0)
            {
                result.IsValid = false;
                result.Errors.Add("Missing fingerprint image data");
            }

            // Validate quality
            var qualityResult = ValidateQuality(qualityScore);
            if (!qualityResult.IsAcceptable)
            {
                result.IsValid = false;
                result.Errors.Add($"Quality too low: {qualityResult.Message}");
            }

            result.QualityValidation = qualityResult;
            return result;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Create a complete fingerprint data package for storage/transmission
        /// </summary>
        /// <param name="template">Raw template bytes</param>
        /// <param name="imageData">Raw image bytes</param>
        /// <param name="qualityScore">Quality score</param>
        /// <param name="imageWidth">Image width</param>
        /// <param name="imageHeight">Image height</param>
        /// <returns>Complete fingerprint package</returns>
        public static FingerprintDataPackage CreateDataPackage(
            byte[] template, byte[] imageData, int qualityScore, int imageWidth, int imageHeight)
        {
            // Validate inputs
            var validation = ValidateFingerprintData(template, imageData, qualityScore);
            if (!validation.IsValid)
            {
                throw new ArgumentException($"Invalid fingerprint data: {string.Join(", ", validation.Errors)}");
            }

            // Convert to PNG for better storage
            byte[] pngImageData;
            try
            {
                pngImageData = ConvertToPng(imageData, imageWidth, imageHeight);
            }
            catch
            {
                // Fallback to original image data if PNG conversion fails
                pngImageData = imageData;
            }

            return new FingerprintDataPackage
            {
                TemplateBase64 = TemplateToBase64(template),
                ImageBase64 = ImageToBase64(pngImageData),
                RawTemplate = template,
                RawImage = pngImageData,
                QualityScore = qualityScore,
                ImageWidth = imageWidth,
                ImageHeight = imageHeight,
                ProcessedAt = DateTime.UtcNow,
                QualityValidation = validation.QualityValidation
            };
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Quality validation result
    /// </summary>
    public class QualityValidationResult
    {
        public int Score { get; set; }
        public QualityLevel Level { get; set; }
        public bool IsAcceptable { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
    }

    /// <summary>
    /// Quality levels
    /// </summary>
    public enum QualityLevel
    {
        Poor,
        Fair,
        Good,
        Excellent
    }

    /// <summary>
    /// Complete fingerprint validation result
    /// </summary>
    public class FingerprintValidationResult
    {
        public bool IsValid { get; set; } = true;
        public List<string> Errors { get; set; } = new List<string>();
        public QualityValidationResult? QualityValidation { get; set; }
    }

    /// <summary>
    /// Complete fingerprint data package
    /// </summary>
    public class FingerprintDataPackage
    {
        public string TemplateBase64 { get; set; } = string.Empty;
        public string ImageBase64 { get; set; } = string.Empty;
        public byte[] RawTemplate { get; set; } = Array.Empty<byte>();
        public byte[] RawImage { get; set; } = Array.Empty<byte>();
        public int QualityScore { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public DateTime ProcessedAt { get; set; }
        public QualityValidationResult? QualityValidation { get; set; }
    }

    #endregion
}