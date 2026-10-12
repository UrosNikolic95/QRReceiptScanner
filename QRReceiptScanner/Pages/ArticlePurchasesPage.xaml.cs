using QRReceiptScanner.Services;

namespace QRReceiptScanner.Pages
{
    /// <summary>Every purchase of one article, newest first.</summary>
    public partial class ArticlePurchasesPage : ContentPage, IQueryAttributable
    {
        public const string ArticleKey = "article";

        public ArticlePurchasesPage()
        {
            InitializeComponent();
        }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (!query.TryGetValue(ArticleKey, out var value) || value is not ArticleSummary article)
                return;

            NameLabel.Text = article.Name;
            SummaryLabel.Text =
                $"{article.Count} {SerbianText.Purchases(article.Count)}  ·  количина {article.Quantity:0.###}  ·  " +
                $"укупно {article.Total:N2} RSD  ·  просек {article.AveragePrice:N2} RSD";
            PurchaseList.ItemsSource = article.Purchases;
        }
    }
}
