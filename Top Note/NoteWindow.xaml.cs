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

            this.Left = note.Left;
            this.Top = note.Top;

            this.Width = note.Width;
            this.Height = note.Height;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e); DragMove();
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);

            if (DataContext is NoteWindowViewModel vm)
            {
                vm.WindowLeft = this.Left;
                vm.WindowTop = this.Top;
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            if (DataContext is NoteWindowViewModel vm)
            {
                vm.WindowWidth = this.Width;
                vm.WindowHeight = this.Height;
            }
        }
    }
}
