using Dapper;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Net.Mime;
using System.Windows;
using System.Windows.Input;
using Top_Note.Models;

namespace Top_Note.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        #region Properties

        public ObservableCollection<NoteModel> Notes { get; } = new ObservableCollection<NoteModel>();

        private bool noNotesErrorMessageBool = false;
        public bool NoNotesErrorMessageBool
        {
            get { return noNotesErrorMessageBool;}
            set
            {
                if (noNotesErrorMessageBool == value)
                {
                    return;
                }
                noNotesErrorMessageBool = value;
                RaisePropertyChanged();
            }
        }

        private bool showListBox = true;
        public bool ShowListBox
        {
            get { return showListBox; }
            set
            {
                if (showListBox == value)
                {
                    return;
                }
                showListBox = value;
                RaisePropertyChanged();
            }
        }

        private bool isDarkMode;

        private string currentTheme = "ThemeLight";

        public string CurrentTheme
        {
            get { return currentTheme; }
            set
            {
                if (currentTheme == value)
                {
                    return;
                }

                currentTheme = value;
                RaisePropertyChanged(nameof(CurrentTheme));
            }
        }

        #endregion

        #region Constuctor

        public MainWindowViewModel()
        {
            ToggleThemeCommand = new RelayCommand(ToggleTheme);
            NewNoteCommand = new RelayCommand(OpenNewNote);
            RefreshListCommand = new RelayCommand(RefreshList);
            DeleteNoteCommand = new RelayCommand<NoteModel>(DeleteNote);

            LoadNotes();
        }

        #endregion

        #region Methods

        private void ToggleTheme()
        {
            isDarkMode = !isDarkMode;
            CurrentTheme = isDarkMode ? "ThemeLight" : "ThemeDark";
            Services.ThemeManager.ApplyTheme(CurrentTheme);
        }

        private void OpenNewNote()
        {
            NoteWindow noteWindow = new NoteWindow();
            noteWindow.Show();
        }

        public void LoadNotes()
        {
            var notesFromDb = SqliteDataAccess.LoadNotes();
            Notes.Clear();
            foreach (var n in notesFromDb)
            {
                Notes.Add(n);
            }
            if (Notes.Count == 0)
            {
                NoNotesErrorMessageBool = true;
                ShowListBox = false;
            }
            else
            {
                NoNotesErrorMessageBool = false;
                ShowListBox = true;
            }
        }

        private void RefreshList()
        {
            LoadNotes();
        }

        private void DeleteNote(NoteModel note)
        {
            if (note == null) return;

            SqliteDataAccess.DeleteNote(note.Id);

            Notes.Remove(note);

            if (Notes.Count == 0)
            {
                NoNotesErrorMessageBool = true;
                ShowListBox = false;
            }

            LoadNotes();
        }
        #endregion

        #region RelayCommands
        public ICommand ToggleThemeCommand { get; }
        public ICommand NewNoteCommand { get; }
        public ICommand RefreshListCommand { get; }
        public ICommand DeleteNoteCommand { get; }
        #endregion
    }
}
