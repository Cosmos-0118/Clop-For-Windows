using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ClopWindows.App.ViewModels;

public sealed class FloatingHudViewModel : ObservableObject
{
    private readonly ObservableCollection<FloatingResultViewModel> _results;

    public FloatingHudViewModel()
    {
        _results = new ObservableCollection<FloatingResultViewModel>();
        _results.CollectionChanged += OnResultsChanged;
    }

    public ObservableCollection<FloatingResultViewModel> Results => _results;

    public bool HasResults => _results.Count > 0;

    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasResults));
    }

    internal void InsertResult(FloatingResultViewModel viewModel)
    {
        _results.Insert(0, viewModel);
        OnPropertyChanged(nameof(HasResults));
    }

    internal void RemoveResult(FloatingResultViewModel viewModel)
    {
        _results.Remove(viewModel);
        OnPropertyChanged(nameof(HasResults));
    }

    internal void ClearResults()
    {
        _results.Clear();
        OnPropertyChanged(nameof(HasResults));
    }
}
