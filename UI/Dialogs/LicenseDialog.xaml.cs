namespace ZephyrsElixir.UI.Dialogs;

public partial class LicenseDialog : Window
{
    private readonly LicenseViewModel _viewModel;

    public LicenseDialog()
    {
        InitializeComponent();
        _viewModel = new LicenseViewModel();
        DataContext = _viewModel;
        Loaded += (_, _) => TxtLicenseKey?.Focus();
        Closed += (_, _) => _viewModel.Dispose();
    }
}
