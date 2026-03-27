using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Windows;

namespace Top_Note
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Verbose;
            base.OnStartup(e);
        }
    }
}
