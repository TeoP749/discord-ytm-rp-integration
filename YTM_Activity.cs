using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using System.Diagnostics;
namespace MediaInfo
{
    class YTM_Activity
    {
        static public (Discord.Activity, Guid) GetYoutubeMusicActivity(string songTitle, string artistName, long startTinme, long endTime)
        {
            Guid guid = Guid.NewGuid();
            string imageUrl = $"https://ytmrpalbumcoverserver.teop.me/album_image/current_album.jpg?id={guid.ToString()}";
            return (new Discord.Activity
            {
                Type = Discord.ActivityType.Listening,
                Details = songTitle,
                State = artistName,
                Instance = true,
                SupportedPlatforms = 1,
                Timestamps =
                {
                    Start = startTinme,
                    End = endTime
                },
                Assets =
                {
                    LargeImage = imageUrl,
                    LargeText = "Album Cover",
                    SmallImage = "ytm_logo",
                    SmallText = "YouTube Music"
                }
            }, guid);
        }
    }
}