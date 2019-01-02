using System.Collections.Generic;

namespace MudaeFarm
{
    public class Config
    {
        public string AuthToken { get; set; }

        public HashSet<string> WishlistCharacters { get; set; } = new HashSet<string>();
    }
}