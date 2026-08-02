using SQLite;

namespace CueGen
{
    [Table("djmdSongPlaylist")]
    public class SongPlaylist : CommonTable
    {
        [PrimaryKey]
        public string ID { get; set; }
        public string PlaylistID { get; set; }
        public string ContentID { get; set; }
        public int? TrackNo { get; set; }
    }
}
