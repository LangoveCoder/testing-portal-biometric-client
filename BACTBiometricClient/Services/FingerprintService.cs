using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BACTBiometricClient.Services
{
    public class FingerprintService
    {
        private IFingerprintScanner? _currentScanner;
        private readonly List<IFingerprintScanner> _availableScanners;

        public int MinimumQualityScore { get; set; } = 60;
        public int MatchThreshold { get; set; } = 70;
        public int CaptureTimeoutSeconds { get; set; } = 30;

        public FingerprintService()
        {
            _availableScanners = new List<IFingerprintScanner>();

            // Register SecuGen scanner
            RegisterScanner(new SecuGenScanner());
        }

        public void RegisterScanner(IFingerprintScanner scanner)
        {
            if (!_availableScanners.Contains(scanner))
            {
                _availableScanners.Add(scanner);
            }
        }

        public List<string> GetAvailableScanners()
        {
            return _availableScanners.Select(s => s.ScannerName).ToList();
        }

        public async Task<ScannerInitResult> InitializeScannerAsync(string scannerName)
        {
            var scanner = _availableScanners.FirstOrDefault(s => s.ScannerName == scannerName);

            if (scanner == null)
            {
                return new ScannerInitResult
                {
                    Success = false,
                    Message = $"Scanner '{scannerName}' not found"
                };
            }

            var result = await scanner.InitializeAsync();

            if (result.Success)
            {
                _currentScanner = scanner;
            }

            return result;
        }

        public async Task<ScannerInitResult> AutoDetectScannerAsync()
        {
            foreach (var scanner in _availableScanners)
            {
                var result = await scanner.InitializeAsync();
                if (result.Success)
                {
                    _currentScanner = scanner;
                    return result;
                }
            }

            return new ScannerInitResult
            {
                Success = false,
                Message = "No fingerprint scanner detected"
            };
        }

        public bool IsReady()
        {
            return _currentScanner != null && _currentScanner.IsConnected;
        }

        public async Task<FingerprintCaptureResult> CaptureAsync()
        {
            if (_currentScanner == null)
            {
                return new FingerprintCaptureResult
                {
                    Success = false,
                    Message = "No scanner initialized",
                    FailureReason = CaptureFailureReason.DeviceNotConnected
                };
            }

            if (!_currentScanner.IsConnected)
            {
                return new FingerprintCaptureResult
                {
                    Success = false,
                    Message = "Scanner not connected",
                    FailureReason = CaptureFailureReason.DeviceNotConnected
                };
            }

            var result = await _currentScanner.CaptureAsync();

            if (!result.Success)
            {
                return result;
            }

            if (result.QualityScore < MinimumQualityScore)
            {
                return new FingerprintCaptureResult
                {
                    Success = false,
                    Message = $"Poor quality (score: {result.QualityScore})",
                    QualityScore = result.QualityScore,
                    FailureReason = CaptureFailureReason.PoorQuality
                };
            }

            return result;
        }

        public async Task<FingerprintMatchResult> VerifyAsync(byte[] storedTemplate, byte[] capturedTemplate)
        {
            if (_currentScanner == null)
            {
                return new FingerprintMatchResult
                {
                    IsMatch = false,
                    Message = "No scanner initialized",
                    Quality = MatchQuality.Poor
                };
            }

            var result = await _currentScanner.MatchAsync(storedTemplate, capturedTemplate);
            result.IsMatch = result.ConfidenceScore >= MatchThreshold;

            if (result.ConfidenceScore >= 90)
                result.Quality = MatchQuality.Excellent;
            else if (result.ConfidenceScore >= 80)
                result.Quality = MatchQuality.Good;
            else if (result.ConfidenceScore >= 70)
                result.Quality = MatchQuality.Fair;
            else
                result.Quality = MatchQuality.Poor;

            return result;
        }

        public int GetQualityScore(byte[] template)
        {
            if (_currentScanner == null || template == null || template.Length == 0)
                return 0;

            return _currentScanner.GetQualityScore(template);
        }

        public ScannerInfo? GetCurrentScannerInfo()
        {
            return _currentScanner?.GetScannerInfo();
        }

        public async Task DisconnectAsync()
        {
            if (_currentScanner != null)
            {
                await _currentScanner.DisconnectAsync();
                // Don't set _currentScanner = null to allow reuse
            }
        }
    }
}