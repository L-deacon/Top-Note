using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Top_Note.Models;

namespace Top_Note
{
    public class SqliteDataAccess
    {
        public static List<NoteModel> LoadNotes()
        {
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                var output = cnn.Query<NoteModel>("SELECT * FROM Notes", new DynamicParameters());
                return output.ToList();
            }
        }

        public static void SaveNote(NoteModel note)
        {
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            { 
                cnn.Execute("INSERT into Notes (Content, IsPinned, Color, Left, Top, FontSize, Width, Height) values (@Content, @IsPinned, @Color, @Left, @Top, @FontSize, @Width, @Height)", note);
            }
        }

        public static void UpdateNote(NoteModel note)
        {
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                cnn.Execute("UPDATE Notes SET Content = @Content, IsPinned = @IsPinned, Color = @Color, Left = @Left, Top = @Top, FontSize = @FontSize, Width = @Width, Height = @Height WHERE Id = @Id", note);
            }
        }

        public static void DeleteNote(int id)
        {
            using (IDbConnection cnn = new SQLiteConnection(LoadConnectionString()))
            {
                cnn.Execute("DELETE FROM Notes WHERE Id = @Id", new { Id = id });
            }
        }

        private static string LoadConnectionString(string id = "Default")
        {
            return ConfigurationManager.ConnectionStrings[id].ConnectionString;
        }
    }
}
