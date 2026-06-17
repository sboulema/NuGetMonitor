using System.Windows.Input;

namespace NuGetMonitor.View.Monitor;

internal sealed partial class NuGetMonitorViewModel
{
    public ICommand RefreshCommand => new DelegateCommand<DataGrid?>(Refresh);

    private void Refresh(DataGrid? dataGrid)
    {
        Load().FireAndForget();
    }
}
