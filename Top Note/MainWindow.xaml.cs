using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Top_Note.ViewModels;

namespace Top_Note
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
        }

        private void NotesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var vm = DataContext as MainWindowViewModel;
            if (NotesListBox.SelectedItem is Top_Note.Models.NoteModel note)
            {
                // open NoteWindow with the selected note for editing
                var w = new NoteWindow(note);
                w.Show();
            }
        }
    }
}