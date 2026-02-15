using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Top_Note.Models;
using Top_Note.ViewModels;

namespace Top_Note
{
    /// <summary>
    /// Interaction logic for NoteWindow.xaml
    /// </summary>
    public partial class NoteWindow : Window
    {
        public NoteWindow()
        {
            InitializeComponent();
            DataContext = new NoteWindowViewModel();
        }

        // overload for editing existing note
        public NoteWindow(NoteModel note)
        {
            InitializeComponent();
            DataContext = new NoteWindowViewModel(note);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e); DragMove();
        }
    }
}
