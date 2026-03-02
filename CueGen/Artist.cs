using SQLite;
using System;

namespace CueGen
{
    [Table("djmdArtist")]
    public class Artist : CommonTable
    {
        [PrimaryKey]
        public string ID { get; set; }
        public string Name { get; set; }
    }
}
