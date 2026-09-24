using QRReceiptScanner.Controls;
using QRReceiptScanner.Services;

namespace QRReceiptScanner.Pages
{
    /// <summary>
    /// Spending over time: the last twelve months, then every year on record.
    ///
    /// The palette is the validated data-visualisation default - one series colour
    /// for every bar, recessive grid and axis ink, and text that never wears the
    /// series colour. Dark mode is a selected set of steps, not a flipped light one.
    /// </summary>
    public partial class StatisticsPage : ContentPage
    {
        private readonly ReceiptStore _store;

        private readonly SpendingBarChart _monthly = new();
        private readonly SpendingBarChart _yearly = new();

        public StatisticsPage(ReceiptStore store)
        {
            InitializeComponent();
            _store = store;

            MonthlyChart.Drawable = _monthly;
            YearlyChart.Drawable = _yearly;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (Application.Current is { } app)
                app.RequestedThemeChanged += OnThemeChanged;

            ApplyTheme();
            await ReloadAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            if (Application.Current is { } app)
                app.RequestedThemeChanged -= OnThemeChanged;
        }

        private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e)
        {
            ApplyTheme();
            Redraw();
        }

        /// <summary>
        /// Chart colours for the theme in force. Both sets were validated against the
        /// surface they actually render on: the page background, white or near-black.
        /// </summary>
        private void ApplyTheme()
        {
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;

            foreach (var chart in new[] { _monthly, _yearly })
            {
                chart.Series = Color.FromArgb(dark ? "#3987e5" : "#2a78d6");
                chart.Surface = Color.FromArgb(dark ? "#1f1f1f" : "#ffffff");
                chart.PrimaryInk = Color.FromArgb(dark ? "#ffffff" : "#0b0b0b");
                chart.MutedInk = Color.FromArgb("#898781");
                chart.GridLine = Color.FromArgb(dark ? "#2c2c2a" : "#e1e0d9");
                chart.Baseline = Color.FromArgb(dark ? "#383835" : "#c3c2b7");
            }
        }

        private async Task ReloadAsync()
        {
            var receipts = await _store.LoadAllAsync();

            EmptyLabel.IsVisible = receipts.Count == 0;
            ChartsHost.IsVisible = receipts.Count > 0;
            if (receipts.Count == 0)
                return;

            var months = SpendingStatistics.LastTwelveMonths(receipts, DateTimeOffset.Now);
            var years = SpendingStatistics.ByYear(receipts);

            _monthly.Buckets = months;
            _yearly.Buckets = years;
            _monthly.SelectedIndex = _yearly.SelectedIndex = -1;

            TotalLabel.Text = $"{receipts.Sum(r => r.TotalAmount):N2} RSD";
            TotalCaption.Text = $"Укупно {receipts.Count} {SerbianText.Receipts(receipts.Count)}, " +
                                $"кроз {years.Count} {SerbianText.Years(years.Count)}.";

            var monthsTotal = months.Sum(m => m.Total);
            var monthsWithSpending = months.Count(m => m.Total > 0m);
            MonthlyCaption.Text = monthsWithSpending == 0
                ? "Нема потрошње у последњих 12 месеци."
                : $"{monthsTotal:N2} RSD, у просеку {monthsTotal / monthsWithSpending:N2} RSD " +
                  $"по месецу ({monthsWithSpending} {SerbianText.Months(monthsWithSpending)} са рачунима).";

            // A chart of one bar says less than the number does; the bars start once
            // there is a second year to compare against.
            var singleYear = years.Count < 2;
            YearlyChart.IsVisible = !singleYear;
            YearlyCaption.Text = singleYear
                ? $"Све у {years[0].AxisLabel}. години: {years[0].Total:N2} RSD. " +
                  "Са рачунима из још једне године овде ће стајати графикон."
                : $"Од {years.First().AxisLabel}. до {years.Last().AxisLabel}. године, " +
                  $"у просеку {years.Sum(y => y.Total) / years.Count:N2} RSD годишње.";

            Redraw();

            // Reloading can grow the page under the scroller, which otherwise leaves
            // the reader partway down instead of at the headline.
            await PageScroll.ScrollToAsync(0, 0, false);
        }

        private void Redraw()
        {
            MonthlyChart.Invalidate();
            YearlyChart.Invalidate();
        }

        private void OnMonthlyPointerMoved(object? sender, PointerEventArgs e) =>
            Select(_monthly, MonthlyChart, e.GetPosition(MonthlyChart));

        private void OnYearlyPointerMoved(object? sender, PointerEventArgs e) =>
            Select(_yearly, YearlyChart, e.GetPosition(YearlyChart));

        private void OnMonthlyTapped(object? sender, TappedEventArgs e) =>
            Select(_monthly, MonthlyChart, e.GetPosition(MonthlyChart));

        private void OnYearlyTapped(object? sender, TappedEventArgs e) =>
            Select(_yearly, YearlyChart, e.GetPosition(YearlyChart));

        private void OnPointerExited(object? sender, PointerEventArgs e)
        {
            // Only the chart the pointer actually left drops its tooltip.
            if (sender is GraphicsView view && view.Drawable is SpendingBarChart chart && chart.SelectedIndex >= 0)
            {
                chart.SelectedIndex = -1;
                view.Invalidate();
            }
        }

        /// <summary>Marks the bar being pointed at, which draws its tooltip.</summary>
        private static void Select(SpendingBarChart chart, GraphicsView view, Point? position)
        {
            if (position is not { } point)
                return;

            var index = chart.HitTest(point);
            if (index == chart.SelectedIndex)
                return;

            chart.SelectedIndex = index;
            view.Invalidate();
        }
    }
}
