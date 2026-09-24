using System.Globalization;
using Microsoft.Extensions.Logging;
using QRReceiptScanner.Pages;
using QRReceiptScanner.Services;
using BarcodeScanning;

namespace QRReceiptScanner
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            // The app speaks Serbian throughout, so its numbers and month names are
            // formatted that way too, whatever locale the phone is set to.
            CultureInfo.DefaultThreadCurrentCulture = SerbianText.Culture;
            CultureInfo.DefaultThreadCurrentUICulture = SerbianText.Culture;
            CultureInfo.CurrentCulture = SerbianText.Culture;
            CultureInfo.CurrentUICulture = SerbianText.Culture;

            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseBarcodeScanning()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // QRRECEIPTS_DIR points the archive somewhere else for a trial run, so
            // sample data never has to be written into the real receipt folder.
            builder.Services.AddSingleton(_ =>
                Environment.GetEnvironmentVariable("QRRECEIPTS_DIR") is { Length: > 0 } folder
                    ? new ReceiptStore(folder)
                    : new ReceiptStore());
            builder.Services.AddSingleton<SufReceiptClient>();
            builder.Services.AddSingleton(_ => new HttpClient());

            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddTransient<ScanPage>();
            builder.Services.AddTransient<BarcodeScanPage>();
            builder.Services.AddTransient<ReceiptListPage>();
            builder.Services.AddTransient<ReceiptDetailPage>();
            builder.Services.AddTransient<StatisticsPage>();

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
