using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Top_Note.Models
{
    public class NoteModel
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public bool IsPinned { get; set; }
        public string Color { get; set; } = "Yellow";
    }
}
