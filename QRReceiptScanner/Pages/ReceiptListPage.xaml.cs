using QRReceiptScanner.Models;
using QRReceiptScanner.Services;

namespace QRReceiptScanner.Pages
{
    public partial class ReceiptListPage : ContentPage
    {
        private readonly ReceiptStore _store;

        public ReceiptListPage(ReceiptStore store)
        {
            InitializeComponent();
            _store = store;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            var receipts = await _store.LoadAllAsync();

            ReceiptList.ItemsSource = receipts;
            EmptyLabel.IsVisible = receipts.Count == 0;
            TotalLabel.Text = receipts.Count == 0
                ? string.Empty
                : $"{receipts.Count} {SerbianText.Receipts(receipts.Count)}  ·  {receipts.Sum(r => r.TotalAmount):N2} RSD";
        }

        private async void OnRefreshing(object? sender, EventArgs e)
        {
            await ReloadAsync();
            Refresher.IsRefreshing = false;
        }

        private async void OnReceiptSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is not Receipt receipt)
                return;

            ReceiptList.SelectedItem = null;

            await Shell.Current.GoToAsync(nameof(ReceiptDetailPage), new Dictionary<string, object>
            {
                [ReceiptDetailPage.ReceiptKey] = receipt,
            });
        }
    }
}
