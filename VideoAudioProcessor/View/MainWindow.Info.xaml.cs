using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoAudioProcessor.Services;

namespace VideoAudioProcessor.View;

public partial class MainWindow
{
    private readonly ObservableCollection<InformationSearchResult> _informationResults = new();

    private void InitializeInformation()
    {
        InformationResultsListBox.ItemsSource = _informationResults;

        _informationSearchService.EnsureIndex();
        RunInformationSearch(null);
    }

    private void ShowInformationScreen()
    {
        ShowScreen(ViewModel.AppScreen.Information);
    }

    private void SearchInformation_Click(object sender, RoutedEventArgs e)
    {
        RunInformationSearch(InformationSearchTextBox.Text);
    }

    private void ShowAllInformation_Click(object sender, RoutedEventArgs e)
    {
        InformationSearchTextBox.Clear();
        RunInformationSearch(null);
    }

    private void InformationSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        RunInformationSearch(InformationSearchTextBox.Text);
        e.Handled = true;
    }

    private void InformationResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateInformationDetails(InformationResultsListBox.SelectedItem as InformationSearchResult);
    }

    private void RunInformationSearch(string? query)
    {
        _informationResults.Clear();
        var limit = string.IsNullOrWhiteSpace(query) ? int.MaxValue : 12;
        foreach (var result in _informationSearchService.Search(query, limit))
        {
            _informationResults.Add(result);
        }

        InformationResultCountText.Text = _informationResults.Count == 0
            ? "Ничего не найдено"
            : $"Найдено результатов: {_informationResults.Count}";

        if (_informationResults.Count == 0)
        {
            UpdateInformationDetails(null);
            return;
        }

        InformationResultsListBox.SelectedIndex = 0;
        UpdateInformationDetails(_informationResults[0]);
    }

    private void UpdateInformationDetails(InformationSearchResult? result)
    {
        if (result == null)
        {
            InformationTitleText.Text = "Результат не выбран";
            InformationLocationText.Text = "Нажмите «Показать всё» или выполните новый поиск.";
            InformationFullText.Text = "Полное описание появится здесь после выбора результата в левой части экрана.";
            return;
        }

        InformationTitleText.Text = result.Title;
        InformationLocationText.Text = $"Где найти: {result.Location}";
        InformationFullText.Text = result.FullText;
    }
}
