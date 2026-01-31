using System;
using System.Threading.Tasks;
using SecuGen.FDxSDKPro.Windows;

namespace BACTBiometricClient.Services
{
    public class SecuGenScanner : IFingerprintScanner
    {
        private static SGFingerPrintManager? _staticDevice = null;
        private static bool _isGloballyInitialized = false;
        private static readonly object _lockObject = new object();

        private SGFingerPrintManager? _fpDevice;
        private bool _isInitialized = false;
        private int _imageWidth = 0;
        private int _imageHeight = 0;
        private int _imageDPI = 0;

        public string ScannerName => "SecuGen Hamster Pro 20";
        public bool IsConnected => _isInitialized && _fpDevice != null;

        public async Task<ScannerInitResult> InitializeAsync()
        {
            return await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("=== SecuGen Scanner Init (Singleton) ===");

                        // If already globally initialized, reuse it
                        if (_isGloballyInitialized && _staticDevice != null)
                        {
                            System.Diagnostics.Debug.WriteLine("Reusing existing scanner connection");
                            _fpDevice = _staticDevice;
                            _isInitialized = true;

                            // Get device info if we don't have it
                            if (_imageWidth == 0)
                            {
                                SGFPMDeviceInfoParam existingDeviceInfo = new SGFPMDeviceInfoParam();
                                int err = _fpDevice.GetDeviceInfo(existingDeviceInfo);
                                if (err == (int)SGFPMError.ERROR_NONE)
                                {
                                    _imageWidth = existingDeviceInfo.ImageWidth;
                                    _imageHeight = existingDeviceInfo.ImageHeight;
                                    _imageDPI = existingDeviceInfo.ImageDPI;
                                }
                            }

                            return new ScannerInitResult
                            {
                                Success = true,
                                Message = $"✓ Scanner already connected!\n\nImage: {_imageWidth}x{_imageHeight}"
                            };
                        }

                        // Clean up any previous instance
                        if (_staticDevice != null)
                        {
                            try
                            {
                                System.Diagnostics.Debug.WriteLine("Cleaning up previous instance...");
                                _staticDevice.CloseDevice();
                                System.Threading.Thread.Sleep(500);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Cleanup warning: {ex.Message}");
                            }
                            _staticDevice = null;
                            _isGloballyInitialized = false;
                        }

                        // Create fresh device instance
                        _fpDevice = new SGFingerPrintManager();
                        _staticDevice = _fpDevice;

                        // Try DEV_FDU08 (U20-A)
                        int initErr = _fpDevice.Init(SGFPMDeviceName.DEV_FDU08);
                        System.Diagnostics.Debug.WriteLine($"Init DEV_FDU08: {initErr} ({GetErrorMessage(initErr)})");

                        // Fallback to AUTO
                        if (initErr != (int)SGFPMError.ERROR_NONE)
                        {
                            System.Diagnostics.Debug.WriteLine("Trying AUTO detection...");
                            _fpDevice = new SGFingerPrintManager();
                            _staticDevice = _fpDevice;
                            initErr = _fpDevice.Init(SGFPMDeviceName.DEV_AUTO);
                            System.Diagnostics.Debug.WriteLine($"Init DEV_AUTO: {initErr}");
                        }

                        if (initErr != (int)SGFPMError.ERROR_NONE)
                        {
                            _staticDevice = null;
                            return new ScannerInitResult
                            {
                                Success = false,
                                Message = "Scanner initialization failed",
                                ErrorDetails = $"Error: {GetErrorMessage(initErr)}\n\n" +
                                             $"1. Unplug scanner from USB\n" +
                                             $"2. Wait 5 seconds\n" +
                                             $"3. Plug scanner back in\n" +
                                             $"4. Restart application\n" +
                                             $"5. Check if Windows Biometric Service is disabled"
                            };
                        }

                        // Open device
                        int openErr = _fpDevice.OpenDevice(0);
                        System.Diagnostics.Debug.WriteLine($"OpenDevice: {openErr}");

                        if (openErr != (int)SGFPMError.ERROR_NONE)
                        {
                            _staticDevice = null;
                            return new ScannerInitResult
                            {
                                Success = false,
                                Message = "Failed to open scanner",
                                ErrorDetails = $"Error: {GetErrorMessage(openErr)}\n\n" +
                                             $"Close any other apps using the scanner"
                            };
                        }

                        // Get device info
                        SGFPMDeviceInfoParam deviceInfo = new SGFPMDeviceInfoParam();
                        int infoErr = _fpDevice.GetDeviceInfo(deviceInfo);

                        if (infoErr == (int)SGFPMError.ERROR_NONE)
                        {
                            _imageWidth = deviceInfo.ImageWidth;
                            _imageHeight = deviceInfo.ImageHeight;
                            _imageDPI = deviceInfo.ImageDPI;
                            System.Diagnostics.Debug.WriteLine($"Device: {_imageWidth}x{_imageHeight} @ {_imageDPI}dpi");
                        }

                        _isInitialized = true;
                        _isGloballyInitialized = true;

                        System.Diagnostics.Debug.WriteLine("✓✓✓ Scanner initialized!");

                        return new ScannerInitResult
                        {
                            Success = true,
                            Message = $"✓ SecuGen U20-A Ready!\n\n" +
                                    $"Image: {_imageWidth}x{_imageHeight}\n" +
                                    $"DPI: {_imageDPI}"
                        };
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"EXCEPTION: {ex.Message}");
                        _staticDevice = null;
                        _isGloballyInitialized = false;

                        return new ScannerInitResult
                        {
                            Success = false,
                            Message = "Critical error",
                            ErrorDetails = $"{ex.GetType().Name}: {ex.Message}"
                        };
                    }
                }
            });
        }

        public async Task<FingerprintCaptureResult> CaptureAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_fpDevice == null || !_isInitialized)
                    {
                        return new FingerprintCaptureResult
                        {
                            Success = false,
                            Message = "Scanner not initialized",
                            FailureReason = CaptureFailureReason.DeviceNotConnected
                        };
                    }

                    System.Diagnostics.Debug.WriteLine("Capturing fingerprint...");

                    byte[] imageBuffer = new byte[_imageWidth * _imageHeight];

                    int err = _fpDevice.GetImage(imageBuffer);

                    if (err != (int)SGFPMError.ERROR_NONE)
                    {
                        string errorMsg = GetErrorMessage(err);
                        System.Diagnostics.Debug.WriteLine($"Capture failed: {errorMsg}");

                        return new FingerprintCaptureResult
                        {
                            Success = false,
                            Message = $"Capture failed: {errorMsg}",
                            FailureReason = err == 4 ? CaptureFailureReason.Timeout : CaptureFailureReason.PoorQuality
                        };
                    }

                    int quality = 0;
                    err = _fpDevice.GetImageQuality(_imageWidth, _imageHeight, imageBuffer, ref quality);

                    System.Diagnostics.Debug.WriteLine($"✓ Captured! Quality: {quality}");

                    byte[] template = new byte[400];
                    err = _fpDevice.CreateTemplate(null, imageBuffer, template);

                    // Clone the image buffer to ensure it persists
                    byte[] imageDataCopy = new byte[imageBuffer.Length];
                    Array.Copy(imageBuffer, imageDataCopy, imageBuffer.Length);

                    // Use FingerprintProcessor for quality validation and feedback
                    var qualityValidation = FingerprintProcessor.ValidateQuality(quality);
                    string qualityMessage = qualityValidation.IsAcceptable 
                        ? $"✓ {qualityValidation.Message}" 
                        : $"⚠ {qualityValidation.Message}";

                    return new FingerprintCaptureResult
                    {
                        Success = true,
                        Template = template,
                        ImageData = imageDataCopy,
                        ImageWidth = _imageWidth,
                        ImageHeight = _imageHeight,
                        QualityScore = quality,
                        Message = qualityMessage
                    };
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Capture exception: {ex.Message}");

                    return new FingerprintCaptureResult
                    {
                        Success = false,
                        Message = $"Error: {ex.Message}",
                        FailureReason = CaptureFailureReason.DeviceError
                    };
                }
            });
        }

        public async Task<FingerprintMatchResult> MatchAsync(byte[] template1, byte[] template2)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_fpDevice == null)
                    {
                        return new FingerprintMatchResult
                        {
                            IsMatch = false,
                            ConfidenceScore = 0,
                            Message = "Scanner not initialized"
                        };
                    }

                    bool matched = false;
                    int err = _fpDevice.MatchTemplate(template1, template2, SGFPMSecurityLevel.ABOVE_NORMAL, ref matched);

                    if (err != (int)SGFPMError.ERROR_NONE)
                    {
                        return new FingerprintMatchResult
                        {
                            IsMatch = false,
                            ConfidenceScore = 0,
                            Message = $"Match error: {GetErrorMessage(err)}"
                        };
                    }

                    // Get actual matching score
                    int score = 0;
                    _fpDevice.GetMatchingScore(template1, template2, ref score);

                    return new FingerprintMatchResult
                    {
                        IsMatch = matched,
                        ConfidenceScore = score > 0 ? score : (matched ? 85 : 0),
                        Message = matched ? "Matched!" : "No match",
                        Quality = matched ? MatchQuality.Excellent : MatchQuality.Poor
                    };
                }
                catch (Exception ex)
                {
                    return new FingerprintMatchResult
                    {
                        IsMatch = false,
                        ConfidenceScore = 0,
                        Message = $"Error: {ex.Message}"
                    };
                }
            });
        }

        public int GetQualityScore(byte[] template)
        {
            return 75;
        }

        public async Task DisconnectAsync()
        {
            await Task.Run(() =>
            {
                System.Diagnostics.Debug.WriteLine("Disconnect called (keeping device open for reuse)");
            });
        }

        public ScannerInfo GetScannerInfo()
        {
            return new ScannerInfo
            {
                Name = "SecuGen Hamster Pro 20",
                Manufacturer = "SecuGen Corporation",
                Model = "HU20-A (FDU08)",
                SerialNumber = "N/A",
                FirmwareVersion = "N/A",
                Type = ScannerType.Optical
            };
        }

        private string GetErrorMessage(int errorCode)
        {
            return errorCode switch
            {
                0 => "ERROR_NONE (Success)",
                1 => "ERROR_CREATION_FAILED",
                2 => "ERROR_FUNCTION_FAILED",
                3 => "ERROR_INVALID_PARAM",
                4 => "ERROR_TIMEOUT (No finger detected)",
                5 => "ERROR_DLLLOAD_FAILED",
                6 => "ERROR_DLLLOAD_FAILED_DRV",
                7 => "ERROR_DLLLOAD_FAILED_ALGO",
                51 => "ERROR_SYSLOAD_FAILED",
                52 => "ERROR_INITIALIZE_FAILED",
                55 => "ERROR_DEVICE_NOT_FOUND",
                56 => "ERROR_DEVICE_ALREADY_OPEN",
                _ => $"UNKNOWN_ERROR ({errorCode})"
            };
        }
    }
}