using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace SwiftSearch.Views
{
    public sealed partial class SearchView : Page
    {
        public SearchView()
        {
            this.InitializeComponent();
        }

        private bool _isInitialLoaded = false;

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            App.SearchService.PropertyChanged += SearchService_PropertyChanged;
            UpdateOverlayState();
            UpdateMemoryStatus();

            if (!_isInitialLoaded)
            {
                _isInitialLoaded = true;
                await TriggerSearchAsync();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            App.SearchService.PropertyChanged -= SearchService_PropertyChanged;
        }

        private void SearchService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(App.SearchService.IsDownloadingModel) || 
                e.PropertyName == nameof(App.SearchService.IsDaemonOnline) ||
                e.PropertyName == nameof(App.SearchService.IsLoadingModel))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateOverlayState();
                    UpdateMemoryStatus();
                });
            }
            else if (e.PropertyName == nameof(App.SearchService.LoadedModels) ||
                     e.PropertyName == nameof(App.SearchService.ActiveModel))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateMemoryStatus();
                });
            }
        }

        private void UpdateOverlayState()
        {
            var service = App.SearchService;
            if (!service.IsDaemonOnline)
            {
                OverlayStatusText.Text = "Connecting to Local Search Engine...";
                OverlaySpinner.IsActive = true;
                OverlayPanel.Visibility = Visibility.Visible;
                SearchTextBox.IsEnabled = false;
                SearchButton.IsEnabled = false;
            }
            else if (service.IsDownloadingModel)
            {
                OverlayStatusText.Text = "Downloading Embedding Engine...";
                OverlaySpinner.IsActive = true;
                OverlayPanel.Visibility = Visibility.Visible;
                SearchTextBox.IsEnabled = false;
                SearchButton.IsEnabled = false;
            }
            else if (service.IsLoadingModel)
            {
                OverlayStatusText.Text = "Loading Local AI Weights...";
                OverlaySpinner.IsActive = true;
                OverlayPanel.Visibility = Visibility.Visible;
                SearchTextBox.IsEnabled = false;
                SearchButton.IsEnabled = false;
            }
            else
            {
                OverlayPanel.Visibility = Visibility.Collapsed;
                SearchTextBox.IsEnabled = true;
                SearchButton.IsEnabled = true;
            }
        }

        private void UpdateMemoryStatus()
        {
            var service = App.SearchService;
            var active = service.ActiveModel;
            var isLoaded = service.LoadedModels.Contains(active);
            
            if (service.IsLoadingModel)
            {
                ModelMemoryIndicator.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 140, 0)); // Orange
                ModelMemoryStatusText.Text = "RAM Memory Loading...";
                QuickLoadUnloadButton.IsEnabled = false;
            }
            else
            {
                QuickLoadUnloadButton.IsEnabled = service.IsDaemonOnline && !service.IsDownloadingModel;
                if (isLoaded)
                {
                    ModelMemoryIndicator.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 46, 204, 113)); // Premium green
                    ModelMemoryStatusText.Text = "Active (RAM Loaded)";
                    QuickLoadUnloadButton.Content = "Unload AI RAM";
                }
                else
                {
                    ModelMemoryIndicator.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 170, 170, 170)); // Gray
                    ModelMemoryStatusText.Text = "RAM Unloaded";
                    QuickLoadUnloadButton.Content = "Load AI to RAM";
                }
            }
        }

        private async void QuickLoadUnloadButton_Click(object sender, RoutedEventArgs e)
        {
            var service = App.SearchService;
            if (!service.IsDaemonOnline) return;
            
            var active = service.ActiveModel;
            var isLoaded = service.LoadedModels.Contains(active);
            
            QuickLoadUnloadButton.IsEnabled = false;
            if (isLoaded)
            {
                await service.UnloadModelFromMemoryAsync(active);
            }
            else
            {
                await service.LoadModelIntoMemoryAsync(active);
            }
            QuickLoadUnloadButton.IsEnabled = true;
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await TriggerSearchAsync();
        }

        private async void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                await TriggerSearchAsync();
            }
        }

        private async Task TriggerSearchAsync()
        {
            string query = SearchTextBox.Text.Trim();
            // If query is empty, we perform NTFS browse only
            bool isBrowseMode = string.IsNullOrEmpty(query);

            SearchButton.IsEnabled = false;
            SearchTextBox.IsEnabled = false;

            // Clear previous bindings and configure initial visibility states
            EverythingListView.ItemsSource = null;
            SemanticListView.ItemsSource = null;

            EverythingSection.Visibility = Visibility.Collapsed;
            SectionSeparator.Visibility = Visibility.Collapsed;
            SemanticSection.Visibility = isBrowseMode ? Visibility.Collapsed : Visibility.Visible;
            SemanticProgressRing.IsActive = !isBrowseMode;
            NoResultsPanel.Visibility = Visibility.Collapsed;
            ResultsScrollViewer.Visibility = Visibility.Visible;

            try
            {
                int topK = 10;
                if (TopKComboBox.SelectedItem is ComboBoxItem selectedItem && 
                    int.TryParse(selectedItem.Tag?.ToString(), out var parsedTopK))
                {
                    topK = parsedTopK;
                }

                if (isBrowseMode)
                {
                    // Instant Browse: 100 files
                    var everythingResults = await App.SearchService.SearchEverythingAsync(string.Empty, 100);
                    bool hasEverything = everythingResults != null && everythingResults.Count > 0;
                    if (hasEverything)
                    {
                        EverythingListView.ItemsSource = everythingResults;
                        EverythingSection.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        EverythingSection.Visibility = Visibility.Collapsed;
                        NoResultsPanel.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    // Regular Hybrid Search: Everything + Semantic
                    // Start both searches in parallel
                    var everythingTask = App.SearchService.SearchEverythingAsync(query, topK);
                    var semanticTask = App.SearchService.SearchAsync(query, topK);

                    // 1. Process Everything Results
                    var everythingResults = await everythingTask;
                    bool hasEverything = everythingResults != null && everythingResults.Count > 0;
                    if (hasEverything)
                    {
                        EverythingListView.ItemsSource = everythingResults;
                        EverythingSection.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        EverythingSection.Visibility = Visibility.Collapsed;
                    }

                    // 2. Process Semantic Results
                    var semanticResults = await semanticTask;
                    SemanticProgressRing.IsActive = false;
                    bool hasSemantic = semanticResults != null && semanticResults.Count > 0;
                    if (hasSemantic)
                    {
                        SemanticListView.ItemsSource = semanticResults;
                        SemanticSection.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        SemanticSection.Visibility = Visibility.Collapsed;
                    }

                    // 3. Manage Separator between sections
                    if (hasEverything && hasSemantic)
                    {
                        SectionSeparator.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        SectionSeparator.Visibility = Visibility.Collapsed;
                    }

                    // 4. Handle overall empty results state
                    if (!hasEverything && !hasSemantic)
                    {
                        NoResultsPanel.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchView] Search execution failed: {ex.Message}");
                SemanticProgressRing.IsActive = false;
                NoResultsPanel.Visibility = Visibility.Visible;
            }
            finally
            {
                SearchButton.IsEnabled = true;
                SearchTextBox.IsEnabled = true;
            }
        }

        private void ResultsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Models.SearchItem item)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(item.FilePath)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SearchView] Failed to open file {item.FilePath}: {ex.Message}");
                }
            }
        }
    }

    public class TextHighlight : DependencyObject
    {
        public static readonly DependencyProperty QueryProperty =
            DependencyProperty.RegisterAttached(
                "Query",
                typeof(string),
                typeof(TextHighlight),
                new PropertyMetadata(string.Empty, OnPropertyChanged));

        public static readonly DependencyProperty FullTextProperty =
            DependencyProperty.RegisterAttached(
                "FullText",
                typeof(string),
                typeof(TextHighlight),
                new PropertyMetadata(string.Empty, OnPropertyChanged));

        public static string GetQuery(DependencyObject obj) => (string)obj.GetValue(QueryProperty);
        public static void SetQuery(DependencyObject obj, string value) => obj.SetValue(QueryProperty, value);

        public static string GetFullText(DependencyObject obj) => (string)obj.GetValue(FullTextProperty);
        public static void SetFullText(DependencyObject obj, string value) => obj.SetValue(FullTextProperty, value);

        private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock textBlock)
            {
                UpdateHighlighting(textBlock);
            }
        }

        private static void UpdateHighlighting(TextBlock textBlock)
        {
            textBlock.Inlines.Clear();
            string fullText = GetFullText(textBlock);
            string query = GetQuery(textBlock);

            if (string.IsNullOrEmpty(fullText))
            {
                return;
            }

            if (string.IsNullOrEmpty(query))
            {
                textBlock.Inlines.Add(new Run { Text = fullText });
                return;
            }

            // Split query into terms, ignore short tokens to avoid high false-positive character highlighting
            var terms = query.Split(new[] { ' ', ',', '.', ';', ':', '-', '_', '\"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(t => t.Trim())
                             .Where(t => t.Length >= 2)
                             .Distinct()
                             .OrderByDescending(t => t.Length)
                             .ToList();

            if (terms.Count == 0)
            {
                textBlock.Inlines.Add(new Run { Text = fullText });
                return;
            }

            // Find all matching segments
            var matches = new List<(int Start, int Length)>();
            foreach (var term in terms)
            {
                int index = 0;
                while ((index = fullText.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) != -1)
                {
                    matches.Add((index, term.Length));
                    index += term.Length;
                }
            }

            if (matches.Count == 0)
            {
                textBlock.Inlines.Add(new Run { Text = fullText });
                return;
            }

            // Sort matches and merge overlapping segments
            var sortedMatches = matches.OrderBy(m => m.Start).ThenByDescending(m => m.Length).ToList();
            var mergedSegments = new List<(int Start, int End)>();

            foreach (var match in sortedMatches)
            {
                int matchEnd = match.Start + match.Length;
                if (mergedSegments.Count == 0)
                {
                    mergedSegments.Add((match.Start, matchEnd));
                }
                else
                {
                    var last = mergedSegments[mergedSegments.Count - 1];
                    if (match.Start <= last.End)
                    {
                        mergedSegments[mergedSegments.Count - 1] = (last.Start, Math.Max(last.End, matchEnd));
                    }
                    else
                    {
                        mergedSegments.Add((match.Start, matchEnd));
                    }
                }
            }

            // Construct inlines
            int currentPos = 0;
            var accentBrush = Application.Current.Resources["AccentAAFillColorDefaultBrush"] as Brush;
            if (accentBrush == null)
            {
                accentBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212)); // Fallback system blue
            }

            foreach (var segment in mergedSegments)
            {
                // Add leading normal text
                if (segment.Start > currentPos)
                {
                    textBlock.Inlines.Add(new Run { Text = fullText.Substring(currentPos, segment.Start - currentPos) });
                }

                // Add highlighted text segment
                string highlightedText = fullText.Substring(segment.Start, segment.End - segment.Start);
                var highlightRun = new Run
                {
                    Text = highlightedText,
                    FontWeight = FontWeights.Bold,
                    Foreground = accentBrush
                };
                textBlock.Inlines.Add(highlightRun);

                currentPos = segment.End;
            }

            // Add trailing normal text
            if (currentPos < fullText.Length)
            {
                textBlock.Inlines.Add(new Run { Text = fullText.Substring(currentPos) });
            }
        }
    }
}
