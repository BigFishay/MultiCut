using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Program
{
    public class Program
    {

        string name;
        string location;
        string description;

        public Program(string name, string location, string description)
        {
            this.name = name;
            this.location = location;
            this.description = description;
        }

        public Program(string json)
        {
            var parts = json.Trim('{', '}').Split(',');
            foreach (var part in parts)
            {
                string[] kv = part.Split(':');
                string key = kv[0].Trim('\"');
                string value = kv[1].Trim('\"');
                switch (key)
                {
                    case "name":
                        name = value;
                        break;
                    case "location":
                        location = value;
                        break;
                    case "description":
                        description = value;
                        break;
                }
            }
            if (name == null || location == null || description == null)
            {
                throw new ArgumentException("Invalid JSON format");
            }
        }

        public string toJson()
        {
            return $"{{\"name\":\"{name}\",\"location\":\"{location}\",\"description\":\"{description}\"}}";
        }

        public override string ToString()
        {
            return location;
        }

    }
}
