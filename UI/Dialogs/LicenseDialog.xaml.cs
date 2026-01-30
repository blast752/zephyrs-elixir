namespace ZephyrsElixir.UI.Views;

public partial class LicenseDialog : Window
{
    private readonly LicenseViewModel _viewModel;

    public LicenseDialog()
    {
        InitializeComponent();
        _viewModel = new LicenseViewModel();
        DataContext = _viewModel;
        Loaded += (_, _) => TxtLicenseKey?.Focus();
        Closed += (_, _) => _viewModel.Cleanup();
    }

    public static bool Show(Window? owner = null)
    {
        var dialog = new LicenseDialog
        {
            Owner = owner ?? Application.Current.MainWindow
        };
        return dialog.ShowDialog() == true;
    }
}