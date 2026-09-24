using System.ComponentModel;
using System.Diagnostics;
using BarcodeScanning;
using QRReceiptScanner.Models;
using QRReceiptScanner.Services;

namespace QRReceiptScanner.Pages
{
    public partial class ScanPage : ContentPage
    {
        /// <summary>
        /// A receipt QR code carries an 850-character link and is therefore dense: about
        /// 100 modules across. The detector wants a few pixels per module, so frames are
        /// analysed at 1920x1080 rather than the 1280x720 default.
        /// </summary>
        private const CaptureQuality FrameQuality = CaptureQuality.High;

        /// <summary>
        /// Starting zoom ratio. A phone lens focuses no closer than about 8 cm, and from
        /// there a 3 cm receipt code covers a quarter of the frame: too few pixels per
        /// module. Zooming 2x fills the frame from a distance the lens can focus at. The
        /// library clamps the request to what the camera supports.
        /// </summary>
        private const float DefaultZoomRatio = 2.0f;

        private readonly SufReceiptClient _client;
        private readonly ReceiptStore _store;

        private CameraView? _camera;
        private IDispatcherTimer? _diagnosticsTimer;

        /// <summary>Guards against the camera firing the same code many times per second.</summary>
        private int _handling;

        // --- Scanner diagnostics, read once a second on the UI thread. ---
        private long _framesAnalysed;
        private long _framesAtLastTick;
        private long _lastTickMs;
        private string _lastDetection = "none yet";

        public ScanPage(SufReceiptClient client, ReceiptStore store)
        {
            InitializeComponent();
            _client = client;
            _store = store;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (_camera is not null)
            {
                _camera.CameraEnabled = true;
                SetScanning(true);
                StartDiagnostics();
                return;
            }

            // Film straight away wherever a camera exists. Scanning is now the only way in,
            // so a machine without a camera can only report that and offer a retry.
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

            StopDiagnostics();

            if (_camera is not null)
            {
                SetScanning(false);
                SetTorch(false);
                _camera.CameraEnabled = false;
            }
        }

        /// <summary>Finds the QR code in a still image and treats it like a camera scan.</summary>
        /// <remarks>Currently unreachable: the "choose an image" button was removed from the page.</remarks>
        private async Task DecodeImageAsync(Stream image, string fileName)
        {
            string? text;
            SetBusy(true, "Тражим QR код на слици…");
            var stopwatch = Stopwatch.StartNew();
            try
            {
                text = await ImageBarcodeDecoder.DecodeQrAsync(image);
            }
            catch (Exception ex)
            {
                Log($"image '{fileName}' failed after {stopwatch.ElapsedMilliseconds} ms: {ex}");
                SetBusy(false, "Слика није могла да се прочита.");
                await DisplayAlertAsync("Слика", $"Слика није могла да се декодира:\n\n{ex.Message}", "У реду");
                return;
            }

            Log($"image '{fileName}' decoded in {stopwatch.ElapsedMilliseconds} ms: " +
                (text is null ? "no QR code" : $"{text.Length} characters"));

            if (string.IsNullOrWhiteSpace(text))
            {
                SetBusy(false, "На слици није пронађен QR код.");
                await DisplayAlertAsync(
                    "QR код није пронађен",
                    "Слика не садржи читљив QR код. Код са рачуна је веома густ, па слика мора бити оштра и " +
                    "равномерно осветљена: држи телефон мирно, сачекај да изоштри код и додај светла или упали " +
                    "лампу. Избегавај сенке и одсјај преко кода.",
                    "У реду");
                return;
            }

            SetBusy(false, "QR код је пронађен.");
            await HandleCodeAsync(text);
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
                    // Receipts carry only a QR code; the other symbologies just add misreads.
                    BarcodeSymbologies = BarcodeFormats.QRCode,
                    CaptureQuality = FrameQuality,
                    CameraFacing = CameraFacing.Back,
                    RequestZoomFactor = DefaultZoomRatio,
                    // Continuous autofocus often settles on the receipt's text rather than
                    // the code; a tap on the preview refocuses on that spot.
                    TapToFocusEnabled = true,
                    VibrationOnDetected = true,
                    // Every analysed frame reports straight away; the first read wins.
                    PoolingInterval = 0,
                    PauseScanning = false,
                    CameraEnabled = true,
                };
                _camera.OnDetectionFinished += OnDetectionFinished;
                _camera.PropertyChanged += OnCameraPropertyChanged;

                CameraHost.Add(_camera);
                CameraMessage.IsVisible = false;
                StartCameraButton.IsVisible = false;
                TorchButton.IsVisible = true;
                StatusLabel.Text = "Држи телефон 10-15 cm од рачуна тако да код испуни преглед. Додирни за изоштравање; у мраку упали лампу.";

                Log($"camera view created, quality {FrameQuality}, zoom request {DefaultZoomRatio:F1}x");
                StartDiagnostics();
            }
            catch (Exception ex)
            {
                Log($"camera failed to start: {ex}");
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
            catch (Exception ex)
            {
                Log($"camera enumeration failed: {ex.Message}");
                return false;
            }
        }
#endif

        private void OnCameraPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // The zoom range arrives once the camera has opened; log it so the trace
            // shows what the default zoom request was clamped to.
            if (e.PropertyName == nameof(CameraView.MaxZoomFactor) && _camera is not null)
                Log($"zoom range {_camera.MinZoomFactor:F1}x-{_camera.MaxZoomFactor:F1}x, current {_camera.CurrentZoomFactor:F1}x");
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
            Log($"torch {(on ? "on" : "off")}");
        }

        /// <summary>Starts or pauses barcode detection; the preview keeps running either way.</summary>
        private void SetScanning(bool scanning)
        {
            if (_camera is not null)
                _camera.PauseScanning = !scanning;
        }

        private void StartDiagnostics()
        {
            if (_diagnosticsTimer is not null)
                return;

            _lastTickMs = Environment.TickCount64;
            _framesAtLastTick = Interlocked.Read(ref _framesAnalysed);

            _diagnosticsTimer = Dispatcher.CreateTimer();
            _diagnosticsTimer.Interval = TimeSpan.FromSeconds(1);
            _diagnosticsTimer.Tick += OnDiagnosticsTick;
            _diagnosticsTimer.Start();
        }

        private void StopDiagnostics()
        {
            if (_diagnosticsTimer is null)
                return;

            _diagnosticsTimer.Stop();
            _diagnosticsTimer.Tick -= OnDiagnosticsTick;
            _diagnosticsTimer = null;
        }

        private void OnDiagnosticsTick(object? sender, EventArgs e)
        {
            var now = Environment.TickCount64;
            var frames = Interlocked.Read(ref _framesAnalysed);
            var elapsedMs = Math.Max(1, now - _lastTickMs);
            var fps = (frames - _framesAtLastTick) * 1000.0 / elapsedMs;
            _framesAtLastTick = frames;
            _lastTickMs = now;

            var scanning = _camera is { PauseScanning: false } ? "yes" : "no";
            var summary =
                $"frames analysed {frames} ({fps:F1}/s), quality {FrameQuality}\n" +
                $"zoom {_camera?.CurrentZoomFactor:F1}x of {_camera?.MinZoomFactor:F1}-{_camera?.MaxZoomFactor:F1}x, scanning: {scanning}\n" +
                $"last detection: {_lastDetection}";

            Log(summary.Replace('\n', ' '));

#if DEBUG
            DiagnosticsLabel.IsVisible = _camera is not null;
            DiagnosticsLabel.Text = summary;
#endif
        }

        private async void OnDetectionFinished(object? sender, OnDetectionFinishedEventArg e)
        {
            // Raised on the UI thread for every analysed frame, with or without a result.
            Interlocked.Increment(ref _framesAnalysed);

            var result = e.BarcodeResults.FirstOrDefault(r => !string.IsNullOrWhiteSpace(ImageBarcodeDecoder.Text(r)));
            if (result is null)
                return;

            var value = ImageBarcodeDecoder.Text(result)!;
            _lastDetection = $"{value.Length} chars at {DateTime.Now:HH:mm:ss}";
            var box = result.ImageBoundingBox;
            Log($"detected {value.Length} chars, format {result.BarcodeFormat}, type {result.BarcodeType}, " +
                $"box {box.X:F0},{box.Y:F0} {box.Width:F0}x{box.Height:F0}: {value}");

            // First recognised frame wins: stop analysing before anything else happens.
            // HandleCodeAsync turns detection back on if the text is not a link.
            SetScanning(false);
            await HandleCodeAsync(value);
        }

        private async Task HandleCodeAsync(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return;

            if (Interlocked.Exchange(ref _handling, 1) == 1)
                return;

            code = code.Trim();

            // A camera can misread, so reject non-links here instead of storing an empty
            // receipt. The scanned text is reported in the alert below, not on the page.
            if (!SufReceiptClient.IsVerificationLink(code))
            {
                Log($"rejected, not a verification link: {code}");

                // Pause the camera so the same misread does not raise a stack of alerts.
                SetScanning(false);

                StatusLabel.Text = "Ово није линк рачуна.";
                await DisplayAlertAsync(
                    "Није линк рачуна",
                    $"Скенирани текст није линк за проверу рачуна:\n\n{code}\n\n" +
                    "QR код са рачуна садржи линк који почиње са https://suf.purs.gov.rs/v/?vl=. " +
                    "Скенирај QR код са рачуна.",
                    "У реду");

                SetScanning(true);

                Interlocked.Exchange(ref _handling, 0);
                return;
            }

            try
            {
                SetScanning(false);

                SetBusy(true, "Учитавам рачун…");

                var receipt = await _client.LoadAsync(code);
                Log($"receipt {receipt.InvoiceNumber}: details loaded {receipt.DetailsLoaded}, error {receipt.LoadError ?? "none"}");

                // Re-scanning a receipt updates the stored copy rather than duplicating it.
                var existing = await _store.FindByInvoiceNumberAsync(receipt.InvoiceNumber);
                if (existing is not null)
                    receipt.Id = existing.Id;

                await _store.SaveAsync(receipt);

                if (receipt.LoadError is not null)
                {
                    await DisplayAlertAsync(
                        "Сачувано без детаља",
                        $"Рачун је сачуван, али Пореска управа није била доступна:\n\n{receipt.LoadError}\n\n" +
                        $"Скенирани линк:\n{code}",
                        "У реду");
                }

                await GoToDetailAsync(receipt);
            }
            catch (Exception ex)
            {
                Log($"handling failed: {ex}");
                await DisplayAlertAsync("Скенирање није успело", ex.Message, "У реду");

                SetScanning(true);
            }
            finally
            {
                SetBusy(false, "Усмери камеру ка QR коду на рачуну.");
                Interlocked.Exchange(ref _handling, 0);
            }
        }

        private async Task GoToDetailAsync(Receipt receipt)
        {
            // ".." pops this page first, so Back from the detail returns to the menu.
            await Shell.Current.GoToAsync($"../{nameof(ReceiptDetailPage)}", new Dictionary<string, object>
            {
                [ReceiptDetailPage.ReceiptKey] = receipt,
            });
        }

        private void SetBusy(bool busy, string status)
        {
            Busy.IsRunning = busy;
            Busy.IsVisible = busy;
            StatusLabel.Text = status;
        }

        /// <summary>
        /// Scanner trace. On Android it goes to logcat under the tag "Scan"
        /// (<c>adb logcat -s Scan</c>), because Debug.WriteLine output never reaches
        /// logcat there; elsewhere it goes to the IDE output window prefixed "[Scan]".
        /// Compiled out of release builds.
        /// </summary>
        [Conditional("DEBUG")]
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
