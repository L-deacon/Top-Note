using Dapper;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Top_Note.Models;
using Color = System.Drawing.Color;

namespace Top_Note.ViewModels
{
    public class NoteWindowViewModel : ViewModelBase
    {
        #region Properties

        private bool isPinned;

        public bool IsPinned
        {
            get { return isPinned; }
            set
            {
                if (IsPinned == value)
                {
                    return;
                }
                else
                {
                    isPinned = value;
                    RaisePropertyChanged();
                }
            }
        }

        private Brush noteColor = Brushes.Yellow;

        public Brush NoteColor
        {
            get { return noteColor; }
            set
            {
                if (noteColor == value)
                {
                    return;
                }
                else
                {
                    noteColor = value;
                    RaisePropertyChanged();
                }
            }
        }

        private string content = string.Empty;
        public string Content
        {
            get { return content; }
            set
            {
                if (content == value)
                {
                    return;
                }
                else
                {
                    content = value;
                    RaisePropertyChanged();
                }
            }
        }

        private string colorHex = "#FFFF00";
        public string ColorHex
        {
            get { return colorHex; }
            set
            {
                if (colorHex == value)
                {
                    return;
                }
                else
                {
                    colorHex = value;
                    RaisePropertyChanged();
                }
            }
        }

        public List<int> FontSizes { get; } = new() {8,16,18,20,22,24};

        private int selectedFontSize = 18;
        public int SelectedFontSize
        {
            get { return selectedFontSize;}
            set
            {
                selectedFontSize = value;
                RaisePropertyChanged();
            }
        }

        private int noteId = 0;


        private double windowLeft;
        public double WindowLeft
        {
            get => windowLeft;
            set { windowLeft = value; RaisePropertyChanged(); }
        }

        private double windowTop;
        public double WindowTop
        {
            get => windowTop;
            set { windowTop = value; RaisePropertyChanged(); }
        }
        private double windowWidth;
        public double WindowWidth
        {
            get => windowWidth;
            set { windowWidth = value; RaisePropertyChanged(); }
        }

        private double windowHeight;
        public double WindowHeight
        {
            get => windowHeight;
            set { windowHeight = value; RaisePropertyChanged(); }
        }


        #endregion

        #region Constructor
        public NoteWindowViewModel()
        {
            TogglePinCommand = new RelayCommand(TogglePin);
            SaveNoteCommand = new RelayCommand(SaveNote);
            ChangeColorCommand = new RelayCommand<string>(ChangeColor);

            ColorHex = "#FFFF00";
        }

        public NoteWindowViewModel(NoteModel note) : this()
        {
            if (note == null) return;
            noteId = note.Id;
            Content = note.Content ?? string.Empty;
            IsPinned = note.IsPinned;
            ColorHex = string.IsNullOrEmpty(note.Color) ? "#FFFF00" : note.Color;
            WindowLeft = note.Left;
            WindowTop = note.Top;
            WindowWidth = note.Width;
            WindowHeight = note.Height;
            SelectedFontSize = note.FontSize > 0 ? note.FontSize : 18;
            try
            {
                NoteColor = (Brush)new BrushConverter().ConvertFromString(ColorHex);
            }
            catch
            {
                NoteColor = Brushes.Yellow;
            }
        }

        #endregion

        #region Methods

        private void TogglePin()
        {
            IsPinned = !IsPinned;
        }

        private void ChangeColor(string hex)
        {
            try
            {
                ColorHex = hex;
                NoteColor = (Brush)new BrushConverter().ConvertFromString(hex);
            }
            catch
            {
                ColorHex = "#FFFF00";
                NoteColor = Brushes.Yellow;
            }
            
        }

        private void SaveNote()
        {
            var note = new NoteModel
            {
                Id = noteId,
                Content = this.Content ?? string.Empty,
                IsPinned = this.IsPinned,
                Color = this.ColorHex ?? "#FFFF00",
                Left = this.WindowLeft,
                Top = this.WindowTop,
                FontSize = this.SelectedFontSize,
                Width = (int)this.WindowWidth,
                Height = (int)this.WindowHeight
            };
            if (note.Content == string.Empty)
            {
                return;
            }

            if (note.Id > 0)
            {
                SqliteDataAccess.UpdateNote(note);
            }
            else
            {
                SqliteDataAccess.SaveNote(note);
            }

            // refresh main list
            var mainVm = Application.Current?.MainWindow?.DataContext as MainWindowViewModel;
            mainVm?.LoadNotes();

            // close the note window that hosts this VM
            foreach (Window w in Application.Current.Windows)
            {
                if (w.DataContext == this)
                {
                    w.Close();
                    break;
                }
            }
        }

        #endregion

        #region RelayCommands 

        public ICommand TogglePinCommand { get; }
        public ICommand SaveNoteCommand { get; }
        public ICommand ChangeColorCommand { get; }

        #endregion

    }
}
