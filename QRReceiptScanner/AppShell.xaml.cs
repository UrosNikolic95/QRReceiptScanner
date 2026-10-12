using QRReceiptScanner.Pages;

namespace QRReceiptScanner
{
    public partial class AppShell : Shell
    {
        public AppShell(MainPage mainPage, IServiceProvider services)
        {
            InitializeComponent();

            HomeContent.Content = mainPage;

            Register<ScanPage>(services);
            Register<BarcodeScanPage>(services);
            Register<ReceiptListPage>(services);
            Register<ReceiptDetailPage>(services);
            Register<StatisticsPage>(services);
            Register<ArticleListPage>(services);
            Register<ArticlePurchasesPage>(services);
        }

        private static void Register<TPage>(IServiceProvider services) where TPage : Page =>
            Routing.RegisterRoute(typeof(TPage).Name, new ContainerRouteFactory<TPage>(services));

        /// <summary>
        /// Resolves routed pages through the container so they can take constructor
        /// dependencies, instead of relying on a parameterless constructor.
        /// </summary>
        private sealed class ContainerRouteFactory<TPage> : RouteFactory where TPage : Page
        {
            private readonly IServiceProvider _services;

            public ContainerRouteFactory(IServiceProvider services) => _services = services;

            public override Element GetOrCreate() => _services.GetRequiredService<TPage>();

            public override Element GetOrCreate(IServiceProvider services) =>
                (services ?? _services).GetRequiredService<TPage>();
        }
    }
}
