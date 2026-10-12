using QRReceiptScanner.Pages;
using QRReceiptScanner.Services;

namespace QRReceiptScanner
{
    public partial class MainPage : ContentPage
    {
        private readonly ReceiptStore _store;

        public MainPage(ReceiptStore store)
        {
            InitializeComponent();
            _store = store;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await RefreshSummaryAsync();
        }

        private async Task RefreshSummaryAsync()
        {
            var receipts = await _store.LoadAllAsync();
            SummaryLabel.Text = receipts.Count == 0
                ? "Још нема сачуваних рачуна."
                : $"{receipts.Count} {SerbianText.Receipts(receipts.Count)}  ·  {receipts.Sum(r => r.TotalAmount):N2} RSD";
        }

        private async void OnScanClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(ScanPage));

        private async void OnListClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(ReceiptListPage));

        private async void OnStatisticsClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(StatisticsPage));

        private async void OnArticlesClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(ArticleListPage));
    }
}
