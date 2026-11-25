using System.Windows;
using PAD.Desktop.ViewModels;

namespace PAD.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
