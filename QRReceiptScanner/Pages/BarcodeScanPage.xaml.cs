using System.Diagnostics;
using BarcodeScanning;
using QRReceiptScanner.Models;
using QRReceiptScanner.Services;

namespace QRReceiptScanner.Pages
{
    /// <summary>
    /// Scans one extra barcode and attaches it to a receipt. Unlike <see cref="ScanPage"/>,
    /// which only ever reads the dense fiscal QR code, this reads the everyday product
    /// symbologies — and only the ones <see cref="BarcodeEncoder"/> can draw again, so
    /// whatever is scanned here can be printed under the slip.
    /// </summary>
    public partial class BarcodeScanPage : ContentPage, IQueryAttributable
    {
        public const string ReceiptKey = "receipt";

        private const BarcodeFormats AcceptedFormats =
            BarcodeFormats.Ean13 | BarcodeFormats.Ean8 | BarcodeFormats.Upca | BarcodeFormats.Code128;

        private readonly ReceiptStore _store;

        private CameraView? _camera;

        /// <summary>Guards against the camera firing the same code many times per second.</summary>
        private int _handling;

        public BarcodeScanPage(ReceiptStore store)
        {
            InitializeComponent();
            _store = store;
        }

        public Receipt? Receipt { get; private set; }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.TryGetValue(ReceiptKey, out var value) && value is Receipt receipt)
                Receipt = receipt;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (_camera is not null)
            {
                _camera.CameraEnabled = true;
                SetScanning(true);
                return;
            }

            if (await HasCameraAsync())
            {
                await StartCameraAsync();
                return;
            }

            StartCameraButton.IsVisible = true;
            CameraMessage.IsVisible = true;
            CameraMessage.Text = "Камера није пронађена.";
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            if (_camera is not null)
            {
                SetScanning(false);
                SetTorch(false);
                _camera.CameraEnabled = false;
            }
        }

        private async void OnStartCameraClicked(object? sender, EventArgs e) => await StartCameraAsync();

        private async Task StartCameraAsync()
        {
            if (!await Methods.AskForRequiredPermissionAsync())
            {
                Log("camera permission denied");
                ShowCameraMessage("Приступ камери је одбијен. Дозволи приступ камери па покушај поново.");
                return;
            }

            try
            {
                _camera = new CameraView
                {
                    BarcodeSymbologies = AcceptedFormats,
                    CaptureQuality = CaptureQuality.High,
                    CameraFacing = CameraFacing.Back,
                    // No zoom boost here: a product barcode is far coarser than the fiscal
                    // QR code, and a wide view makes it easier to line up.
                    TapToFocusEnabled = true,
                    VibrationOnDetected = true,
                    PoolingInterval = 0,
                    PauseScanning = false,
                    CameraEnabled = true,
                };
                _camera.OnDetectionFinished += OnDetectionFinished;

                CameraHost.Add(_camera);
                CameraMessage.IsVisible = false;
                StartCameraButton.IsVisible = false;
                TorchButton.IsVisible = true;
                StatusLabel.Text = "Усмери камеру ка бар коду на производу или гаранцији.";

                Log($"barcode camera created, formats {AcceptedFormats}");
            }
            catch (Exception ex)
            {
                Log($"barcode camera failed to start: {ex}");
                _camera = null;
                CameraHost.Clear();
                ShowCameraMessage($"Камера није могла да се покрене ({ex.Message}).");
            }
        }

        private void ShowCameraMessage(string message)
        {
            CameraMessage.IsVisible = true;
            CameraMessage.Text = message;
            StartCameraButton.IsVisible = false;
        }

        private async void OnDetectionFinished(object? sender, OnDetectionFinishedEventArg e)
        {
            var result = e.BarcodeResults.FirstOrDefault(r => !string.IsNullOrWhiteSpace(ImageBarcodeDecoder.Text(r)));
            if (result is null)
                return;

            // First recognised frame wins: stop analysing before anything else happens.
            if (Interlocked.Exchange(ref _handling, 1) == 1)
                return;

            SetScanning(false);

            var value = ImageBarcodeDecoder.Text(result)!.Trim();
            var format = result.BarcodeFormat.ToString();
            Log($"extra barcode detected, format {format}: {value}");

            await AddAsync(value, format);
        }

        private async Task AddAsync(string value, string format)
        {
            if (Receipt is not { } receipt)
            {
                Log("no receipt to attach the barcode to");
                await Shell.Current.GoToAsync("..");
                return;
            }

            var added = new ExtraBarcode { Value = value, Format = format };
            try
            {
                receipt.ExtraBarcodes.Add(added);
                await _store.SaveAsync(receipt);
                Log($"saved extra barcode on receipt {receipt.Id}");
            }
            catch (Exception ex)
            {
                // Put the receipt back the way it was rather than leave it holding a
                // barcode that never reached the disk.
                receipt.ExtraBarcodes.Remove(added);
                Log($"saving extra barcode failed: {ex}");
                await DisplayAlertAsync("Чување није успело", ex.Message, "У реду");
                _handling = 0;
                SetScanning(true);
                return;
            }

            await Shell.Current.GoToAsync("..");
        }

        private void OnTorchClicked(object? sender, EventArgs e)
        {
            if (_camera is null)
                return;

            SetTorch(!_camera.TorchOn);
        }

        private void SetTorch(bool on)
        {
            if (_camera is null)
                return;

            _camera.TorchOn = on;
            TorchButton.Text = on ? "Угаси лампу" : "Упали лампу";
        }

        /// <summary>Starts or pauses detection; the preview keeps running either way.</summary>
        private void SetScanning(bool scanning)
        {
            if (_camera is not null)
                _camera.PauseScanning = !scanning;
        }

        /// <summary>
        /// Whether the device has any camera. The scanner library assumes one exists, so
        /// this is asked of the platform directly; desktop machines often have none.
        /// </summary>
        private static Task<bool> HasCameraAsync()
        {
#if ANDROID
            var pm = Android.App.Application.Context.PackageManager;
            return Task.FromResult(pm?.HasSystemFeature(Android.Content.PM.PackageManager.FeatureCameraAny) == true);
#elif IOS || MACCATALYST
            return Task.FromResult(AVFoundation.AVCaptureDevice.GetDefaultDevice(AVFoundation.AVMediaTypes.Video) is not null);
#elif WINDOWS
            return HasWindowsCameraAsync();
#else
            return Task.FromResult(false);
#endif
        }

#if WINDOWS
        private static async Task<bool> HasWindowsCameraAsync()
        {
            try
            {
                var cameras = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
                    Windows.Devices.Enumeration.DeviceClass.VideoCapture);
                return cameras.Count > 0;
            }
            catch
            {
                return false;
            }
        }
#endif

        private static void Log(string message)
        {
#if ANDROID
            Android.Util.Log.Info("Scan", message);
#else
            Debug.WriteLine($"[Scan] {message}");
#endif
        }
    }
}
