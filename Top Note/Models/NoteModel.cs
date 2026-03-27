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
        public double Left { get; set; }
        public double Top { get; set; }
        public int FontSize { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

    }
}
