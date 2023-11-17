using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MCServer.Models
{
    public class Properties : List<Property>
    {

    }

    public class Property
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public List<string> Comments { get; set; } = new();

        public Property(string Name, string Value, List<string> Comments)
        {
            this.Name = Name;
            this.Value = Value;
            this.Comments = Comments;
        }
    }
}
