using System.Threading.Tasks;

namespace BACTBiometricClient.Services
{
    public interface IFingerprintScanner
    {
        string ScannerName { get; }
        bool IsConnected { get; }
        Task<ScannerInitResult> InitializeAsync();
        Task<FingerprintCaptureResult> CaptureAsync();
        Task<FingerprintMatchResult> MatchAsync(byte[] template1, byte[] template2);
        int GetQualityScore(byte[] template);
        Task DisconnectAsync();
        ScannerInfo GetScannerInfo();
    }

    public class ScannerInitResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ErrorDetails { get; set; }
    }

    public class FingerprintCaptureResult
    {
        public bool Success { get; set; }
        public byte[]? Template { get; set; }
        public byte[]? ImageData { get; set; }
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public int QualityScore { get; set; }
        public string Message { get; set; } = string.Empty;
        public CaptureFailureReason? FailureReason { get; set; }
    }

    public class FingerprintMatchResult
    {
        public bool IsMatch { get; set; }
        public int ConfidenceScore { get; set; }
        public string Message { get; set; } = string.Empty;
        public MatchQuality Quality { get; set; }
    }

    public class ScannerInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string FirmwareVersion { get; set; } = string.Empty;
        public ScannerType Type { get; set; }
    }

    public enum CaptureFailureReason
    {
        DeviceNotConnected,
        Timeout,
        PoorQuality,
        DeviceError,
        UserCancelled
    }

    public enum MatchQuality
    {
        Excellent,
        Good,
        Fair,
        Poor
    }

    public enum ScannerType
    {
        Optical,
        Capacitive,
        Ultrasonic,
        Thermal
    }
}