using QRReceiptScanner.Services;

namespace QRReceiptScanner.Pages
{
    /// <summary>Every article from every receipt, most money spent first.</summary>
    public partial class ArticleListPage : ContentPage
    {
        private readonly ReceiptStore _store;

        public ArticleListPage(ReceiptStore store)
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
            var articles = ArticleStatistics.FromReceipts(await _store.LoadAllAsync());

            ArticleList.ItemsSource = articles;
            EmptyLabel.IsVisible = articles.Count == 0;
            TotalLabel.Text = articles.Count == 0
                ? string.Empty
                : $"{articles.Count} {SerbianText.Articles(articles.Count)}  ·  {articles.Sum(a => a.Total):N2} RSD";
        }

        private async void OnRefreshing(object? sender, EventArgs e)
        {
            await ReloadAsync();
            Refresher.IsRefreshing = false;
        }

        private async void OnArticleSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is not ArticleSummary article)
                return;

            ArticleList.SelectedItem = null;

            await Shell.Current.GoToAsync(nameof(ArticlePurchasesPage), new Dictionary<string, object>
            {
                [ArticlePurchasesPage.ArticleKey] = article,
            });
        }
    }
}
