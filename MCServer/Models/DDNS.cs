namespace MCServer.Models
{
    public class DDNS
    {
        public const string IP = "{IP}";
        public const string SUB = "{SUB}";

        public static List<DDNS> DDNSList { get; } = new()
        {
            new("None",""){ ID = 0 },
            new("Google",$"https://domains.google.com/nic/update?hostname={SUB}"){ ID = 1 },
        };

        public static DDNS Find(int ID) => DDNSList.Find(x => x.ID == ID);

        //------------------------------------------------------------------------------------//

        public int ID { get; set; }
        public string Name { get; set; }
        public string Domain { get; set; }

        public DDNS(string Name, string Domain)
        {
            this.Name = Name;
            this.Domain = Domain;
        }

        public string GetURL(string SUB = "", string IP = "")
        {
            return Domain.Replace(DDNS.SUB, SUB).Replace(DDNS.IP, IP);
        }
    }
}
